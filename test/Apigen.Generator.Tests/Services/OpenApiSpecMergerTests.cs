using Apigen.Generator.Services;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace Apigen.Generator.Tests.Services;

public class OpenApiSpecMergerTests
{
  [Fact]
  public void Merge_CombinesPaths()
  {
    var doc1 = CreateDoc("Identity", new() { ["/connect/token"] = new OpenApiPathItem() });
    var doc2 = CreateDoc("Vault", new() { ["/ciphers"] = new OpenApiPathItem() });

    var merged = OpenApiSpecMerger.Merge(new[] { doc1, doc2 });

    Assert.Equal(2, merged.Paths.Count);
    Assert.Contains("/connect/token", merged.Paths.Keys);
    Assert.Contains("/ciphers", merged.Paths.Keys);
  }

  [Fact]
  public void Merge_CombinesSchemas_NoDuplicates()
  {
    var doc1 = CreateDocWithSchema("Identity", "KdfType", CreateEnumSchema());
    var doc2 = CreateDocWithSchema("Vault", "CipherModel", CreateObjectSchema("name"));

    var merged = OpenApiSpecMerger.Merge(new[] { doc1, doc2 });

    Assert.Equal(2, merged.Components.Schemas.Count);
    Assert.Contains("KdfType", merged.Components.Schemas.Keys);
    Assert.Contains("CipherModel", merged.Components.Schemas.Keys);
  }

  [Fact]
  public void Merge_DeduplicatesIdenticalSchemas()
  {
    var schema1 = CreateObjectSchema("email");
    var schema2 = CreateObjectSchema("email");

    var doc1 = CreateDocWithSchema("Identity", "UserModel", schema1);
    var doc2 = CreateDocWithSchema("Vault", "UserModel", schema2);

    var merged = OpenApiSpecMerger.Merge(new[] { doc1, doc2 });

    Assert.Single(merged.Components.Schemas);
    Assert.Contains("UserModel", merged.Components.Schemas.Keys);
  }

  [Fact]
  public void Merge_HandlesConflictingSchemas()
  {
    var schema1 = CreateObjectSchema("email");
    var schema2 = CreateObjectSchema("name");

    var doc1 = CreateDocWithSchema("Identity API", "UserModel", schema1);
    var doc2 = CreateDocWithSchema("Vault API", "UserModel", schema2);

    var merged = OpenApiSpecMerger.Merge(new[] { doc1, doc2 });

    Assert.Equal(2, merged.Components.Schemas.Count);
    Assert.Contains("UserModel", merged.Components.Schemas.Keys);
    // Second one should be prefixed with second word from title
    Assert.Contains("APIUserModel", merged.Components.Schemas.Keys);
  }

  [Fact]
  public void Merge_CombinesSecuritySchemes()
  {
    var doc1 = new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = "Doc1", Version = "1.0" },
      Paths = new OpenApiPaths(),
      Components = new OpenApiComponents
      {
        SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
          ["bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer" }
        }
      }
    };
    var doc2 = new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = "Doc2", Version = "1.0" },
      Paths = new OpenApiPaths(),
      Components = new OpenApiComponents
      {
        SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
          ["oauth2"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.OAuth2 }
        }
      }
    };

    var merged = OpenApiSpecMerger.Merge(new[] { doc1, doc2 });

    Assert.Equal(2, merged.Components.SecuritySchemes.Count);
  }

  [Fact]
  public void Merge_SingleDoc_ReturnsSameDoc()
  {
    var doc = CreateDoc("Test", new() { ["/users"] = new OpenApiPathItem() });

    var merged = OpenApiSpecMerger.Merge(new[] { doc });

    Assert.Single(merged.Paths);
    Assert.Contains("/users", merged.Paths.Keys);
  }

  [Fact]
  public void Merge_EmptyDocs_ThrowsArgumentException()
  {
    Assert.Throws<ArgumentException>(() => OpenApiSpecMerger.Merge(Array.Empty<OpenApiDocument>()));
  }

  [Fact]
  public void Merge_ThreeSpecs_CombinesAll()
  {
    var doc1 = CreateDoc("Identity", new() { ["/identity/connect"] = new OpenApiPathItem() });
    var doc2 = CreateDoc("Vault", new() { ["/api/ciphers"] = new OpenApiPathItem(), ["/api/folders"] = new OpenApiPathItem() });
    var doc3 = CreateDoc("Public", new() { ["/public/org"] = new OpenApiPathItem() });

    var merged = OpenApiSpecMerger.Merge(new[] { doc1, doc2, doc3 });

    Assert.Equal(4, merged.Paths.Count);
  }

  [Fact]
  public void Merge_CombinesTitles()
  {
    var doc1 = CreateDoc("Identity", new());
    var doc2 = CreateDoc("Vault", new());

    var merged = OpenApiSpecMerger.Merge(new[] { doc1, doc2 });

    Assert.Equal("Identity + Vault", merged.Info.Title);
  }

  [Fact]
  public void AreSchemasStructurallyEqual_IdenticalSchemas_ReturnsTrue()
  {
    var a = CreateObjectSchema("email");
    var b = CreateObjectSchema("email");

    Assert.True(OpenApiSpecMerger.AreSchemasStructurallyEqual(a, b));
  }

  [Fact]
  public void AreSchemasStructurallyEqual_DifferentProperties_ReturnsFalse()
  {
    var a = CreateObjectSchema("email");
    var b = CreateObjectSchema("name");

    Assert.False(OpenApiSpecMerger.AreSchemasStructurallyEqual(a, b));
  }

  [Fact]
  public void AreSchemasStructurallyEqual_DifferentTypes_ReturnsFalse()
  {
    var a = new OpenApiSchema { Type = JsonSchemaType.Object };
    var b = new OpenApiSchema { Type = JsonSchemaType.String };

    Assert.False(OpenApiSpecMerger.AreSchemasStructurallyEqual(a, b));
  }

  [Fact]
  public void Merge_NullComponents_HandlesGracefully()
  {
    var doc1 = new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = "Doc1", Version = "1.0" },
      Paths = new OpenApiPaths { ["/test"] = new OpenApiPathItem() },
      Components = null
    };
    var doc2 = CreateDoc("Doc2", new() { ["/test2"] = new OpenApiPathItem() });

    var merged = OpenApiSpecMerger.Merge(new[] { doc1, doc2 });

    Assert.Equal(2, merged.Paths.Count);
  }

  // Helper methods
  private static OpenApiDocument CreateDoc(string title, OpenApiPaths paths)
  {
    return new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = title, Version = "1.0" },
      Paths = paths,
      Components = new OpenApiComponents()
    };
  }

  private static OpenApiDocument CreateDocWithSchema(string title, string schemaName, OpenApiSchema schema)
  {
    var components = new OpenApiComponents();
    components.Schemas = new Dictionary<string, IOpenApiSchema> { [schemaName] = schema };
    return new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = title, Version = "1.0" },
      Paths = new OpenApiPaths(),
      Components = components
    };
  }

  private static OpenApiSchema CreateObjectSchema(string propertyName)
  {
    return new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        [propertyName] = new OpenApiSchema { Type = JsonSchemaType.String }
      }
    };
  }

  private static OpenApiSchema CreateEnumSchema()
  {
    return new OpenApiSchema
    {
      Type = JsonSchemaType.Integer,
      Enum = new List<JsonNode?>
      {
        JsonValue.Create(0),
        JsonValue.Create(1),
      }
    };
  }
}
