using Apigen.Generator.Generators;
using Apigen.Generator.Models;
using Microsoft.OpenApi;

namespace Apigen.Generator.Tests.Generators;

/// <summary>
/// Object schemas written inline as a property have no name in the spec. The generator derives
/// one instead of degrading the property to <c>object</c>.
/// </summary>
public class InlineObjectModelTests : IDisposable
{
  private readonly string _output = Path.Combine(Path.GetTempPath(), $"apigen-inline-{Guid.NewGuid():N}");

  public void Dispose()
  {
    if (Directory.Exists(_output))
    {
      Directory.Delete(_output, true);
    }
  }

  private static OpenApiSchema PermissionsSchema() => new()
  {
    Type = JsonSchemaType.Object,
    Properties = new Dictionary<string, IOpenApiSchema>
    {
      ["users"] = new OpenApiSchema
      {
        Type = JsonSchemaType.Array,
        Items = new OpenApiSchema { Type = JsonSchemaType.Integer }
      }
    }
  };

  private static OpenApiDocument DocumentWith(params (string Schema, string Property, OpenApiSchema Value)[] properties)
  {
    OpenApiDocument doc = new()
    {
      Info = new OpenApiInfo { Title = "Test API", Version = "1.0" },
      Paths = new OpenApiPaths(),
      Components = new OpenApiComponents { Schemas = new Dictionary<string, IOpenApiSchema>() }
    };

    foreach ((string schemaName, string propertyName, OpenApiSchema value) in properties)
    {
      if (!doc.Components.Schemas.TryGetValue(schemaName, out IOpenApiSchema? existing))
      {
        existing = new OpenApiSchema
        {
          Type = JsonSchemaType.Object,
          Properties = new Dictionary<string, IOpenApiSchema>()
        };
        doc.Components.Schemas[schemaName] = existing;
      }

      ((OpenApiSchema)existing).Properties![propertyName] = value;
    }

    return doc;
  }

  private async Task<Dictionary<string, string>> GenerateAsync(OpenApiDocument doc, GeneratorConfiguration? config = null)
  {
    config ??= new GeneratorConfiguration();
    config.OutputPath = _output;
    config.Models.ProjectName = "Models";

    GeneratorOptions options = config.ToGeneratorOptions();
    options.OutputPath = _output;
    options.ProjectName = "Models";

    ModelGenerator generator = new(options, config);
    await generator.GenerateModelsAsync(doc);

    return Directory.GetFiles(Path.Combine(_output, "Models"), "*.cs")
      .ToDictionary(file => Path.GetFileNameWithoutExtension(file) ?? file, File.ReadAllText);
  }

  [Fact]
  public async Task InlineObjectProperty_GeneratesNamedType()
  {
    Dictionary<string, string> files = await GenerateAsync(
      DocumentWith(("Correspondent", "set_permissions", PermissionsSchema())));

    Assert.Contains("CorrespondentSetPermissions", files.Keys);
    Assert.Contains("public CorrespondentSetPermissions? SetPermissions", files["Correspondent"]);
    Assert.Contains("public List<int>? Users", files["CorrespondentSetPermissions"]);
  }

  [Fact]
  public async Task IdenticalInlineObjects_ShareOneType()
  {
    Dictionary<string, string> files = await GenerateAsync(DocumentWith(
      ("Correspondent", "set_permissions", PermissionsSchema()),
      ("Tag", "set_permissions", PermissionsSchema())));

    Assert.Contains("CorrespondentSetPermissions", files.Keys);
    Assert.DoesNotContain("TagSetPermissions", files.Keys);
    Assert.Contains("public CorrespondentSetPermissions? SetPermissions", files["Tag"]);
  }

  [Fact]
  public async Task InlineTypeName_OverridesDerivedName()
  {
    GeneratorConfiguration config = new();
    config.InlineTypeNames.Add(new InlineTypeName
    {
      PropertyFilter = "^set_permissions$",
      Name = "PermissionsSet"
    });

    Dictionary<string, string> files = await GenerateAsync(
      DocumentWith(("Correspondent", "set_permissions", PermissionsSchema())), config);

    Assert.Contains("PermissionsSet", files.Keys);
    Assert.DoesNotContain("CorrespondentSetPermissions", files.Keys);
    Assert.Contains("public PermissionsSet? SetPermissions", files["Correspondent"]);
  }

  [Fact]
  public async Task InlineTypeName_SchemaFilterLimitsTheOverride()
  {
    GeneratorConfiguration config = new();
    config.InlineTypeNames.Add(new InlineTypeName
    {
      PropertyFilter = "^set_permissions$",
      SchemaFilter = "^Nothing$",
      Name = "PermissionsSet"
    });

    Dictionary<string, string> files = await GenerateAsync(
      DocumentWith(("Correspondent", "set_permissions", PermissionsSchema())), config);

    Assert.Contains("CorrespondentSetPermissions", files.Keys);
    Assert.DoesNotContain("PermissionsSet", files.Keys);
  }

  [Fact]
  public async Task InlineObjectMatchingNamedSchema_ReusesThatType()
  {
    OpenApiDocument doc = DocumentWith(("RemoveDnsEntryRequest", "dnsEntry", PermissionsSchema()));
    doc.Components!.Schemas!["DnsEntry"] = PermissionsSchema();

    Dictionary<string, string> files = await GenerateAsync(doc);

    Assert.Contains("public DnsEntry? DnsEntry", files["RemoveDnsEntryRequest"]);
    Assert.DoesNotContain("RemoveDnsEntryRequestDnsEntry", files.Keys);
  }

  [Fact]
  public async Task FreeFormObjectProperty_StaysObject()
  {
    Dictionary<string, string> files = await GenerateAsync(DocumentWith(
      ("Task", "extra_data", new OpenApiSchema { Type = JsonSchemaType.Object })));

    Assert.Contains("public object? ExtraData", files["Task"]);
  }

  [Fact]
  public async Task NestedInlineObject_GeneratesTypeForEachLevel()
  {
    OpenApiSchema nested = new()
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema> { ["view"] = PermissionsSchema() }
    };

    Dictionary<string, string> files = await GenerateAsync(
      DocumentWith(("Correspondent", "permissions", nested)));

    Assert.Contains("CorrespondentPermissions", files.Keys);
    Assert.Contains("CorrespondentPermissionsView", files.Keys);
    Assert.Contains("public CorrespondentPermissionsView? View", files["CorrespondentPermissions"]);
  }
}
