using Microsoft.OpenApi;
using System.Text.Json.Nodes;
using Apigen.Generator.Services;
using Apigen.Generator.Models;

namespace Apigen.Generator.Tests.Services;

public class ModelDeduplicatorTests
{
  /// <summary>
  /// Creates a minimal OpenApiDocument with the given schemas and paths that reference them.
  /// Each schema entry maps a schema name to its properties (name u2192 type).
  /// Each path entry maps a path to operations referencing schemas.
  /// </summary>
  private static OpenApiDocument CreateDocument(
    Dictionary<string, OpenApiSchema> schemas,
    OpenApiPaths? paths = null)
  {
    var components = new OpenApiComponents();
    components.Schemas = new Dictionary<string, IOpenApiSchema>();
    foreach (var kvp in schemas)
    {
      components.Schemas[kvp.Key] = kvp.Value;
    }
    return new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = "Test API", Version = "1.0" },
      Paths = paths ?? new OpenApiPaths(),
      Components = components
    };
  }

  /// <summary>
  /// Creates a simple schema with string properties.
  /// </summary>
  private static OpenApiSchema CreateSchema(
    string[] propertyNames,
    string[]? required = null)
  {
    OpenApiSchema schema = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>()
    };

    foreach (string name in propertyNames)
    {
      schema.Properties[name] = new OpenApiSchema { Type = JsonSchemaType.String };
    }

    if (required != null)
    {
      schema.Required = new HashSet<string>(required);
    }

    return schema;
  }

  /// <summary>
  /// Creates paths that make schemas appear as request bodies via POST.
  /// This ensures schemas are detected as "used in requests" by SchemaUsageAnalyzer.
  /// </summary>
  private static OpenApiPaths CreateRequestPaths(params string[] schemaNames)
  {
    OpenApiPaths paths = new OpenApiPaths();

    for (int i = 0; i < schemaNames.Length; i++)
    {
      string schemaName = schemaNames[i];
      OpenApiPathItem pathItem = new OpenApiPathItem
      {
        Operations = new Dictionary<HttpMethod, OpenApiOperation>
        {
          [HttpMethod.Post] = new OpenApiOperation
          {
            OperationId = $"create_{schemaName}_{i}",
            RequestBody = new OpenApiRequestBody
            {
              Content = new Dictionary<string, IOpenApiMediaType>
              {
                ["application/json"] = new OpenApiMediaType
                {
                  Schema = new OpenApiSchema
                  {
                    Id = schemaName
                  }
                }
              }
            },
            Responses = new OpenApiResponses
            {
              ["200"] = new OpenApiResponse { Description = "OK" }
            }
          }
        }
      };

      paths[$"/api/{schemaName.ToLower()}/{i}"] = pathItem;
    }

    return paths;
  }

  /// <summary>
  /// Runs the full pipeline: SchemaUsageAnalyzer u2192 SchemaVariantGenerator u2192 ModelDeduplicator u2192 DeduplicateAcrossSchemas
  /// </summary>
  private static Dictionary<string, ModelGenerationDecision> RunFullPipeline(OpenApiDocument document)
  {
    SchemaUsageAnalyzer usageAnalyzer = new SchemaUsageAnalyzer(document);
    Dictionary<string, SchemaUsage> usageMap = usageAnalyzer.Analyze();

    SchemaVariantGenerator variantGenerator = new SchemaVariantGenerator(document, usageMap);
    Dictionary<string, Dictionary<SchemaVariantType, SchemaVariant>> variants = variantGenerator.GenerateVariants();

    ModelDeduplicator deduplicator = new ModelDeduplicator(usageMap, variants);
    Dictionary<string, ModelGenerationDecision> decisions = deduplicator.MakeDecisions();

    deduplicator.DeduplicateAcrossSchemas();

    return decisions;
  }

  [Fact]
  public void TwoIdenticalUnrelatedSchemas_NeitherIsSkipped()
  {
    // Arrange: Two schemas with identical properties but no naming relationship
    OpenApiSchema schemaA = CreateSchema(new[] { "name", "description" }, new[] { "name" });
    OpenApiSchema schemaB = CreateSchema(new[] { "name", "description" }, new[] { "name" });

    OpenApiPaths paths = CreateRequestPaths("Tag", "LongTagName");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["Tag"] = schemaA,
        ["LongTagName"] = schemaB
      },
      paths);

    // Act
    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    // Assert: unrelated schemas are NOT deduped even if structurally identical
    Assert.False(decisions["Tag"].SkipGeneration);
    Assert.False(decisions["LongTagName"].SkipGeneration);
  }

  [Fact]
  public void TwoSchemasWithDifferentProperties_NeitherIsSkipped()
  {
    // Arrange: Two schemas with different properties
    OpenApiSchema schemaA = CreateSchema(new[] { "name", "description" });
    OpenApiSchema schemaB = CreateSchema(new[] { "title", "summary" });

    OpenApiPaths paths = CreateRequestPaths("Foo", "Bar");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["Foo"] = schemaA,
        ["Bar"] = schemaB
      },
      paths);

    // Act
    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    // Assert: neither is skipped
    Assert.False(decisions["Foo"].SkipGeneration);
    Assert.Null(decisions["Foo"].CanonicalSchemaName);

    Assert.False(decisions["Bar"].SkipGeneration);
    Assert.Null(decisions["Bar"].CanonicalSchemaName);
  }

  [Fact]
  public void TwoSchemasWithDifferentRequiredFields_NeitherIsSkipped()
  {
    // Arrange: Same property names but different required fields
    OpenApiSchema schemaA = CreateSchema(new[] { "name", "description" }, new[] { "name" });
    OpenApiSchema schemaB = CreateSchema(new[] { "name", "description" }, new[] { "name", "description" });

    OpenApiPaths paths = CreateRequestPaths("Foo", "Bar");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["Foo"] = schemaA,
        ["Bar"] = schemaB
      },
      paths);

    // Act
    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    // Assert: different required fields u2192 different structure hash u2192 neither is skipped
    Assert.False(decisions["Foo"].SkipGeneration);
    Assert.Null(decisions["Foo"].CanonicalSchemaName);

    Assert.False(decisions["Bar"].SkipGeneration);
    Assert.Null(decisions["Bar"].CanonicalSchemaName);
  }

  [Fact]
  public void PatchedPrefixSchema_IdenticalToNonPatched_PatchedIsSkipped()
  {
    // Arrange: PatchedFooRequest identical to FooRequest
    OpenApiSchema fooSchema = CreateSchema(new[] { "name", "value" }, new[] { "name" });
    OpenApiSchema patchedFooSchema = CreateSchema(new[] { "name", "value" }, new[] { "name" });

    OpenApiPaths paths = CreateRequestPaths("FooRequest", "PatchedFooRequest");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["FooRequest"] = fooSchema,
        ["PatchedFooRequest"] = patchedFooSchema
      },
      paths);

    // Act
    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    // Assert: PatchedFooRequest is skipped, FooRequest is canonical
    Assert.False(decisions["FooRequest"].SkipGeneration);
    Assert.Null(decisions["FooRequest"].CanonicalSchemaName);

    Assert.True(decisions["PatchedFooRequest"].SkipGeneration);
    Assert.Equal("FooRequest", decisions["PatchedFooRequest"].CanonicalSchemaName);
  }

  [Fact]
  public void Idempotent_RunningTwiceProducesSameResult()
  {
    // Arrange: PatchedFoo is naming-related to Foo, so it will be deduped
    OpenApiSchema schemaA = CreateSchema(new[] { "name", "value" }, new[] { "name" });
    OpenApiSchema schemaB = CreateSchema(new[] { "name", "value" }, new[] { "name" });

    OpenApiPaths paths = CreateRequestPaths("FooRequest", "PatchedFooRequest");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["FooRequest"] = schemaA,
        ["PatchedFooRequest"] = schemaB
      },
      paths);

    // Act: Run pipeline and call DeduplicateAcrossSchemas twice
    SchemaUsageAnalyzer usageAnalyzer = new SchemaUsageAnalyzer(document);
    Dictionary<string, SchemaUsage> usageMap = usageAnalyzer.Analyze();

    SchemaVariantGenerator variantGenerator = new SchemaVariantGenerator(document, usageMap);
    Dictionary<string, Dictionary<SchemaVariantType, SchemaVariant>> variants = variantGenerator.GenerateVariants();

    ModelDeduplicator deduplicator = new ModelDeduplicator(usageMap, variants);
    Dictionary<string, ModelGenerationDecision> decisions = deduplicator.MakeDecisions();

    deduplicator.DeduplicateAcrossSchemas();

    // Capture state after first run
    bool fooSkip1 = decisions["FooRequest"].SkipGeneration;
    string? fooCanonical1 = decisions["FooRequest"].CanonicalSchemaName;
    bool patchedSkip1 = decisions["PatchedFooRequest"].SkipGeneration;
    string? patchedCanonical1 = decisions["PatchedFooRequest"].CanonicalSchemaName;

    // Run again
    deduplicator.DeduplicateAcrossSchemas();

    // Assert: same result after second run
    Assert.Equal(fooSkip1, decisions["FooRequest"].SkipGeneration);
    Assert.Equal(fooCanonical1, decisions["FooRequest"].CanonicalSchemaName);
    Assert.Equal(patchedSkip1, decisions["PatchedFooRequest"].SkipGeneration);
    Assert.Equal(patchedCanonical1, decisions["PatchedFooRequest"].CanonicalSchemaName);
  }

  [Fact]
  public void TwoSchemasWithDifferentNullability_NeitherIsSkipped()
  {
    // Arrange: Same property names/types but different nullable status
    OpenApiSchema schemaA = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["name"] = new OpenApiSchema { Type = JsonSchemaType.String },
        ["value"] = new OpenApiSchema { Type = JsonSchemaType.Integer }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["name"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
        ["value"] = new OpenApiSchema { Type = JsonSchemaType.Integer }
      }
    };

    OpenApiPaths paths = CreateRequestPaths("Foo", "Bar");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["Foo"] = schemaA,
        ["Bar"] = schemaB
      },
      paths);

    // Act
    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    // Assert: different nullable u2192 different structure hash u2192 neither is skipped
    Assert.False(decisions["Foo"].SkipGeneration);
    Assert.Null(decisions["Foo"].CanonicalSchemaName);

    Assert.False(decisions["Bar"].SkipGeneration);
    Assert.Null(decisions["Bar"].CanonicalSchemaName);
  }

  [Fact]
  public void TwoSchemasWithDifferentMaxLength_NeitherIsSkipped()
  {
    // Arrange: Same property names/types but different maxLength
    OpenApiSchema schemaA = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["name"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 128 }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["name"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 256 }
      }
    };

    OpenApiPaths paths = CreateRequestPaths("Foo", "Bar");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["Foo"] = schemaA,
        ["Bar"] = schemaB
      },
      paths);

    // Act
    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    // Assert: different maxLength u2192 different structure hash u2192 neither is skipped
    Assert.False(decisions["Foo"].SkipGeneration);
    Assert.False(decisions["Bar"].SkipGeneration);
  }

  [Fact]
  public void TwoSchemasWithDifferentReadOnly_NeitherIsSkipped()
  {
    OpenApiSchema schemaA = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["id"] = new OpenApiSchema { Type = JsonSchemaType.Integer, ReadOnly = true },
        ["name"] = new OpenApiSchema { Type = JsonSchemaType.String }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["id"] = new OpenApiSchema { Type = JsonSchemaType.Integer, ReadOnly = false },
        ["name"] = new OpenApiSchema { Type = JsonSchemaType.String }
      }
    };

    OpenApiPaths paths = CreateRequestPaths("Foo", "Bar");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["Foo"] = schemaA,
        ["Bar"] = schemaB
      },
      paths);

    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    Assert.False(decisions["Foo"].SkipGeneration);
    Assert.False(decisions["Bar"].SkipGeneration);
  }

  [Fact]
  public void TwoSchemasWithDifferentWriteOnly_NeitherIsSkipped()
  {
    OpenApiSchema schemaA = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["password"] = new OpenApiSchema { Type = JsonSchemaType.String, WriteOnly = true }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["password"] = new OpenApiSchema { Type = JsonSchemaType.String, WriteOnly = false }
      }
    };

    OpenApiPaths paths = CreateRequestPaths("Foo", "Bar");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["Foo"] = schemaA,
        ["Bar"] = schemaB
      },
      paths);

    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    Assert.False(decisions["Foo"].SkipGeneration);
    Assert.False(decisions["Bar"].SkipGeneration);
  }

  [Fact]
  public void TwoSchemasWithDifferentDefault_NeitherIsSkipped()
  {
    OpenApiSchema schemaA = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["enabled"] = new OpenApiSchema
        {
          Type = JsonSchemaType.Boolean,
          Default = JsonValue.Create(true)
        }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["enabled"] = new OpenApiSchema
        {
          Type = JsonSchemaType.Boolean,
          Default = JsonValue.Create(false)
        }
      }
    };

    OpenApiPaths paths = CreateRequestPaths("Foo", "Bar");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["Foo"] = schemaA,
        ["Bar"] = schemaB
      },
      paths);

    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    Assert.False(decisions["Foo"].SkipGeneration);
    Assert.False(decisions["Bar"].SkipGeneration);
  }

  [Fact]
  public void TwoSchemasWithDifferentEnumValues_NeitherIsSkipped()
  {
    OpenApiSchema schemaA = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["status"] = new OpenApiSchema
        {
          Type = JsonSchemaType.String,
          Enum = new List<JsonNode?>
          {
            JsonValue.Create("active"),
            JsonValue.Create("inactive")
          }
        }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["status"] = new OpenApiSchema
        {
          Type = JsonSchemaType.String,
          Enum = new List<JsonNode?>
          {
            JsonValue.Create("active"),
            JsonValue.Create("deleted")
          }
        }
      }
    };

    OpenApiPaths paths = CreateRequestPaths("Foo", "Bar");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["Foo"] = schemaA,
        ["Bar"] = schemaB
      },
      paths);

    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    Assert.False(decisions["Foo"].SkipGeneration);
    Assert.False(decisions["Bar"].SkipGeneration);
  }

  [Fact]
  public void TwoSchemasWithDifferentMinMaxRange_NeitherIsSkipped()
  {
    OpenApiSchema schemaA = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["score"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "0", Maximum = "100" }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = JsonSchemaType.Object,
      Properties = new Dictionary<string, IOpenApiSchema>
      {
        ["score"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Minimum = "0", Maximum = "1000" }
      }
    };

    OpenApiPaths paths = CreateRequestPaths("Foo", "Bar");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["Foo"] = schemaA,
        ["Bar"] = schemaB
      },
      paths);

    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    Assert.False(decisions["Foo"].SkipGeneration);
    Assert.False(decisions["Bar"].SkipGeneration);
  }

  [Fact]
  public void ThreeIdenticalUnrelatedSchemas_NoneAreSkipped()
  {
    // Arrange: Three schemas with identical properties but no naming relationship
    OpenApiSchema schemaA = CreateSchema(new[] { "name", "email" }, new[] { "name" });
    OpenApiSchema schemaB = CreateSchema(new[] { "name", "email" }, new[] { "name" });
    OpenApiSchema schemaC = CreateSchema(new[] { "name", "email" }, new[] { "name" });

    OpenApiPaths paths = CreateRequestPaths("User", "UserCopy", "UserDuplicate");

    OpenApiDocument document = CreateDocument(
      new Dictionary<string, OpenApiSchema>
      {
        ["User"] = schemaA,
        ["UserCopy"] = schemaB,
        ["UserDuplicate"] = schemaC
      },
      paths);

    // Act
    Dictionary<string, ModelGenerationDecision> decisions = RunFullPipeline(document);

    // Assert: unrelated schemas are NOT deduped even if structurally identical
    Assert.False(decisions["User"].SkipGeneration);
    Assert.False(decisions["UserCopy"].SkipGeneration);
    Assert.False(decisions["UserDuplicate"].SkipGeneration);
  }
}
