using Microsoft.OpenApi.Models;
using Apigen.Generator.Services;
using Apigen.Generator.Models;

namespace Apigen.Generator.Tests.Services;

public class ModelDeduplicatorTests
{
  /// <summary>
  /// Creates a minimal OpenApiDocument with the given schemas and paths that reference them.
  /// Each schema entry maps a schema name to its properties (name → type).
  /// Each path entry maps a path to operations referencing schemas.
  /// </summary>
  private static OpenApiDocument CreateDocument(
    Dictionary<string, OpenApiSchema> schemas,
    OpenApiPaths? paths = null)
  {
    return new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = "Test API", Version = "1.0" },
      Paths = paths ?? new OpenApiPaths(),
      Components = new OpenApiComponents
      {
        Schemas = schemas
      }
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
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>()
    };

    foreach (string name in propertyNames)
    {
      schema.Properties[name] = new OpenApiSchema { Type = "string" };
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
        Operations = new Dictionary<Microsoft.OpenApi.Models.OperationType, OpenApiOperation>
        {
          [Microsoft.OpenApi.Models.OperationType.Post] = new OpenApiOperation
          {
            OperationId = $"create_{schemaName}_{i}",
            RequestBody = new OpenApiRequestBody
            {
              Content = new Dictionary<string, OpenApiMediaType>
              {
                ["application/json"] = new OpenApiMediaType
                {
                  Schema = new OpenApiSchema
                  {
                    Reference = new OpenApiReference
                    {
                      Type = ReferenceType.Schema,
                      Id = schemaName
                    }
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
  /// Runs the full pipeline: SchemaUsageAnalyzer → SchemaVariantGenerator → ModelDeduplicator → DeduplicateAcrossSchemas
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

    // Assert: different required fields → different structure hash → neither is skipped
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
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["name"] = new OpenApiSchema { Type = "string", Nullable = false },
        ["value"] = new OpenApiSchema { Type = "integer", Nullable = false }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["name"] = new OpenApiSchema { Type = "string", Nullable = true },
        ["value"] = new OpenApiSchema { Type = "integer", Nullable = false }
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

    // Assert: different nullable → different structure hash → neither is skipped
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
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["name"] = new OpenApiSchema { Type = "string", MaxLength = 128 }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["name"] = new OpenApiSchema { Type = "string", MaxLength = 256 }
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

    // Assert: different maxLength → different structure hash → neither is skipped
    Assert.False(decisions["Foo"].SkipGeneration);
    Assert.False(decisions["Bar"].SkipGeneration);
  }

  [Fact]
  public void TwoSchemasWithDifferentReadOnly_NeitherIsSkipped()
  {
    OpenApiSchema schemaA = new OpenApiSchema
    {
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["id"] = new OpenApiSchema { Type = "integer", ReadOnly = true },
        ["name"] = new OpenApiSchema { Type = "string" }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["id"] = new OpenApiSchema { Type = "integer", ReadOnly = false },
        ["name"] = new OpenApiSchema { Type = "string" }
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
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["password"] = new OpenApiSchema { Type = "string", WriteOnly = true }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["password"] = new OpenApiSchema { Type = "string", WriteOnly = false }
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
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["enabled"] = new OpenApiSchema
        {
          Type = "boolean",
          Default = new Microsoft.OpenApi.Any.OpenApiBoolean(true)
        }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["enabled"] = new OpenApiSchema
        {
          Type = "boolean",
          Default = new Microsoft.OpenApi.Any.OpenApiBoolean(false)
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
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["status"] = new OpenApiSchema
        {
          Type = "string",
          Enum = new List<Microsoft.OpenApi.Any.IOpenApiAny>
          {
            new Microsoft.OpenApi.Any.OpenApiString("active"),
            new Microsoft.OpenApi.Any.OpenApiString("inactive")
          }
        }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["status"] = new OpenApiSchema
        {
          Type = "string",
          Enum = new List<Microsoft.OpenApi.Any.IOpenApiAny>
          {
            new Microsoft.OpenApi.Any.OpenApiString("active"),
            new Microsoft.OpenApi.Any.OpenApiString("deleted")
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
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["score"] = new OpenApiSchema { Type = "integer", Minimum = 0, Maximum = 100 }
      }
    };

    OpenApiSchema schemaB = new OpenApiSchema
    {
      Type = "object",
      Properties = new Dictionary<string, OpenApiSchema>
      {
        ["score"] = new OpenApiSchema { Type = "integer", Minimum = 0, Maximum = 1000 }
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
