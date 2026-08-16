using Apigen.Generator.Generators;
using Apigen.Generator.Models;
using Microsoft.OpenApi;

namespace Apigen.Generator.Tests.Generators;

/// <summary>
/// The resource is already part of the call path (<c>client.Documents.…</c>), so method names
/// carry only what the operation adds. These tests pin down how that name is derived.
/// </summary>
public class MethodNamingTests : IDisposable
{
  private readonly string _output = Path.Combine(Path.GetTempPath(), $"apigen-naming-{Guid.NewGuid():N}");

  public void Dispose()
  {
    if (Directory.Exists(_output))
    {
      Directory.Delete(_output, true);
    }
  }

  private static void AddOperation(OpenApiDocument doc, string path, HttpMethod method, string tag, string? operationId, OpenApiSchema? responseSchema = null)
  {
    if (!doc.Paths.TryGetValue(path, out IOpenApiPathItem? item))
    {
      item = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
      doc.Paths[path] = item;
    }

    ((OpenApiPathItem)item).Operations![method] = new OpenApiOperation
    {
      OperationId = operationId,
      Tags = new HashSet<OpenApiTagReference> { new(tag, doc) },
      Responses = responseSchema == null
        ? new OpenApiResponses { ["204"] = new OpenApiResponse() }
        : new OpenApiResponses
        {
          ["200"] = new OpenApiResponse
          {
            Content = new Dictionary<string, IOpenApiMediaType>
            {
              ["application/json"] = new OpenApiMediaType { Schema = responseSchema }
            }
          }
        }
    };
  }

  private static OpenApiSchema CollectionSchema() => new()
  {
    Type = JsonSchemaType.Array,
    Items = new OpenApiSchema { Type = JsonSchemaType.Object }
  };

  private async Task<string> GenerateInterfaceAsync(string interfaceName, params (string Path, HttpMethod Method, string Tag, string? OperationId)[] operations)
  {
    OpenApiDocument doc = new()
    {
      Info = new OpenApiInfo { Title = "Test API", Version = "1.0" },
      Paths = new OpenApiPaths(),
      Components = new OpenApiComponents()
    };

    foreach ((string path, HttpMethod method, string tag, string? operationId) in operations)
    {
      // A GET on a collection route only counts as List when a collection comes back
      bool isCollectionRoute = method == HttpMethod.Get && !path.TrimEnd('/').EndsWith("}");
      AddOperation(doc, path, method, tag, operationId, isCollectionRoute ? CollectionSchema() : null);
    }

    ClientGenerator generator = new(new ClientGenerationOptions { ProjectName = "Client" }, new CodeFormattingOptions());
    await generator.GenerateAsync(doc, _output);

    return await File.ReadAllTextAsync(Path.Combine(_output, "Client", $"{interfaceName}.cs"));
  }

  [Fact]
  public async Task CrudRoutes_WithTrailingSlash_GetIdiomaticNames()
  {
    string generated = await GenerateInterfaceAsync(
      "IDocumentsClient",
      ("/api/documents/", HttpMethod.Get, "documents", "documents_list"),
      ("/api/documents/", HttpMethod.Post, "documents", "documents_create"),
      ("/api/documents/{id}/", HttpMethod.Get, "documents", "documents_retrieve"),
      ("/api/documents/{id}/", HttpMethod.Put, "documents", "documents_update"),
      ("/api/documents/{id}/", HttpMethod.Delete, "documents", "documents_destroy"));

    Assert.Contains(" ListAsync(", generated);
    Assert.Contains(" CreateAsync(", generated);
    Assert.Contains(" GetAsync(", generated);
    Assert.Contains(" UpdateAsync(", generated);
    Assert.Contains(" DeleteAsync(", generated);
  }

  [Fact]
  public async Task SingletonRoute_IsGetNotList()
  {
    OpenApiDocument doc = new()
    {
      Info = new OpenApiInfo { Title = "Test API", Version = "1.0" },
      Paths = new OpenApiPaths(),
      Components = new OpenApiComponents()
    };

    // GET /api/config returns one object, not a collection
    AddOperation(doc, "/api/config", HttpMethod.Get, "config", "Config_Get", new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["version"] = new OpenApiSchema { Type = JsonSchemaType.String }
      }
    });

    ClientGenerator generator = new(new ClientGenerationOptions { ProjectName = "Client" }, new CodeFormattingOptions());
    await generator.GenerateAsync(doc, _output);

    string generated = await File.ReadAllTextAsync(Path.Combine(_output, "Client", "IConfigClient.cs"));

    Assert.Contains(" GetAsync(", generated);
    Assert.DoesNotContain(" ListAsync(", generated);
  }

  [Fact]
  public async Task SubResourceRoute_KeepsItsOwnName()
  {
    string generated = await GenerateInterfaceAsync(
      "IDocumentsClient",
      ("/api/documents/{id}/", HttpMethod.Delete, "documents", "documents_destroy"),
      ("/api/documents/{id}/versions/{version_id}/", HttpMethod.Delete, "documents", "documents_delete_version"));

    // Deleting a version is not another way of deleting a document
    Assert.Contains(" DeleteVersionAsync(", generated);
    Assert.Single(System.Text.RegularExpressions.Regex.Matches(generated, @" DeleteAsync\("));
  }

  [Fact]
  public async Task BulkOperations_KeepTheirOperationId()
  {
    string generated = await GenerateInterfaceAsync(
      "IDocumentsClient",
      ("/api/documents/bulk_download/", HttpMethod.Post, "documents", "bulk_download"),
      ("/api/documents/bulk_edit/", HttpMethod.Post, "documents", "bulk_edit"));

    Assert.Contains(" BulkDownloadAsync(", generated);
    Assert.Contains(" BulkEditAsync(", generated);
    Assert.DoesNotContain(" BulkAsync(", generated);
  }

  [Fact]
  public async Task RedundantResourceName_IsStripped()
  {
    string generated = await GenerateInterfaceAsync(
      "IClientsClient",
      ("/api/v1/clients/bulk", HttpMethod.Post, "clients", "bulkClients"),
      ("/api/v1/clients/{id}/edit", HttpMethod.Get, "clients", "editClient"));

    Assert.Contains(" BulkAsync(", generated);
    Assert.Contains(" EditAsync(", generated);
  }

  [Fact]
  public async Task ExcludedOperation_IsNotGenerated()
  {
    OpenApiDocument doc = new()
    {
      Info = new OpenApiInfo { Title = "Test API", Version = "1.0" },
      Paths = new OpenApiPaths(),
      Components = new OpenApiComponents()
    };

    AddOperation(doc, "/api/folders/{id}", HttpMethod.Put, "folders", "Folders_Put");
    AddOperation(doc, "/api/folders/{id}", HttpMethod.Post, "folders", "Folders_PostPut");

    GeneratorConfiguration config = new();
    config.ExcludeOperations.Add(new ExcludeOperation
    {
      OperationIdFilter = "_PostPut$",
      Reason = "Verb alias for PUT"
    });

    ClientGenerator generator = new(
      new ClientGenerationOptions { ProjectName = "Client" },
      new CodeFormattingOptions(),
      config: config);
    await generator.GenerateAsync(doc, _output);

    string generated = await File.ReadAllTextAsync(Path.Combine(_output, "Client", "IFoldersClient.cs"));

    Assert.Contains(" UpdateAsync(", generated);
    Assert.DoesNotContain(" PostPutAsync(", generated);
  }

  [Fact]
  public async Task OperationIdOverride_WinsFromDerivedName()
  {
    OpenApiDocument doc = new()
    {
      Info = new OpenApiInfo { Title = "Test API", Version = "1.0" },
      Paths = new OpenApiPaths(),
      Components = new OpenApiComponents()
    };

    AddOperation(doc, "/api/documents/delete/", HttpMethod.Post, "documents", "documents_delete");

    NamingOptions naming = new();
    naming.OperationIdOverrides["documents_delete"] = "BulkDelete";

    ClientGenerator generator = new(new ClientGenerationOptions { ProjectName = "Client" }, new CodeFormattingOptions(), naming);
    await generator.GenerateAsync(doc, _output);

    string generated = await File.ReadAllTextAsync(Path.Combine(_output, "Client", "IDocumentsClient.cs"));

    Assert.Contains(" BulkDeleteAsync(", generated);
  }
}
