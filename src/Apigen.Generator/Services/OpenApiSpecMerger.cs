using Microsoft.OpenApi.Models;

namespace Apigen.Generator.Services;

/// <summary>
/// Merges multiple OpenAPI documents into a single document.
/// Combines paths, schemas, and security schemes with smart deduplication.
/// </summary>
public static class OpenApiSpecMerger
{
  /// <summary>
  /// Merge multiple OpenAPI documents into one.
  /// - Paths are combined (must be unique after prefix application)
  /// - Schemas are deduplicated if structurally identical, or prefixed if conflicting
  /// - Security schemes are unioned
  /// </summary>
  public static OpenApiDocument Merge(IEnumerable<OpenApiDocument> documents)
  {
    List<OpenApiDocument> docs = documents.ToList();

    if (docs.Count == 0)
      throw new ArgumentException("At least one document is required", nameof(documents));

    if (docs.Count == 1)
      return docs[0];

    var merged = new OpenApiDocument
    {
      Info = new OpenApiInfo
      {
        Title = string.Join(" + ", docs.Select(d => d.Info.Title)),
        Version = docs[0].Info.Version,
      },
      Paths = new OpenApiPaths(),
      Components = new OpenApiComponents
      {
        Schemas = new Dictionary<string, OpenApiSchema>(),
        SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>(),
      },
    };

    // Track schema origins for conflict resolution
    Dictionary<string, (OpenApiSchema Schema, string SourceTitle)> schemaOrigins = new();

    foreach (var doc in docs)
    {
      // Merge paths
      if (doc.Paths != null)
      {
        foreach (var path in doc.Paths)
        {
          if (merged.Paths.ContainsKey(path.Key))
          {
            Console.WriteLine($"Warning: Duplicate path '{path.Key}' found when merging specs. Keeping first occurrence.");
          }
          else
          {
            merged.Paths[path.Key] = path.Value;
          }
        }
      }

      // Merge schemas with deduplication
      if (doc.Components?.Schemas != null)
      {
        foreach (var schema in doc.Components.Schemas)
        {
          if (!schemaOrigins.ContainsKey(schema.Key))
          {
            // First time seeing this schema name
            schemaOrigins[schema.Key] = (schema.Value, doc.Info.Title);
            merged.Components.Schemas[schema.Key] = schema.Value;
          }
          else
          {
            // Schema name already exists -- check if structurally identical
            if (!AreSchemasStructurallyEqual(schemaOrigins[schema.Key].Schema, schema.Value))
            {
              // Conflict: different structure, same name
              string prefixedName = GenerateConflictName(schema.Key, doc.Info.Title);
              Console.WriteLine($"Warning: Schema '{schema.Key}' differs between specs. Adding as '{prefixedName}' from '{doc.Info.Title}'.");
              merged.Components.Schemas[prefixedName] = schema.Value;

              // Update $ref references in this doc's paths to point to the prefixed name
              UpdateSchemaReferences(doc, schema.Key, prefixedName);
            }
            // else: identical schema, skip (deduplicated)
          }
        }
      }

      // Merge security schemes
      if (doc.Components?.SecuritySchemes != null)
      {
        foreach (var scheme in doc.Components.SecuritySchemes)
        {
          if (!merged.Components.SecuritySchemes.ContainsKey(scheme.Key))
          {
            merged.Components.SecuritySchemes[scheme.Key] = scheme.Value;
          }
        }
      }
    }

    return merged;
  }

  /// <summary>
  /// Compare two schemas for structural equality (same properties, types, required fields)
  /// </summary>
  public static bool AreSchemasStructurallyEqual(OpenApiSchema a, OpenApiSchema b)
  {
    if (a.Type != b.Type) return false;

    // Compare enum values
    if ((a.Enum?.Count ?? 0) != (b.Enum?.Count ?? 0)) return false;

    // Compare properties count
    if ((a.Properties?.Count ?? 0) != (b.Properties?.Count ?? 0)) return false;

    if (a.Properties != null && b.Properties != null)
    {
      foreach (var prop in a.Properties)
      {
        if (!b.Properties.TryGetValue(prop.Key, out var bProp))
          return false;
        if (prop.Value.Type != bProp.Type)
          return false;
        if (prop.Value.Format != bProp.Format)
          return false;
        if (prop.Value.Nullable != bProp.Nullable)
          return false;
      }
    }

    // Compare required fields
    var aRequired = a.Required?.OrderBy(r => r).ToList() ?? new List<string>();
    var bRequired = b.Required?.OrderBy(r => r).ToList() ?? new List<string>();
    if (!aRequired.SequenceEqual(bRequired)) return false;

    // Compare additionalProperties flag
    if (a.AdditionalPropertiesAllowed != b.AdditionalPropertiesAllowed) return false;

    return true;
  }

  /// <summary>
  /// Generate a non-conflicting name for a duplicate schema
  /// </summary>
  private static string GenerateConflictName(string originalName, string sourceTitle)
  {
    // Extract a short prefix from the source title
    // e.g., "Vaultwarden Identity 1.35.4" -> "Identity"
    string[] words = sourceTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    string prefix = words.Length > 1 ? words[1] : words[0];

    // Clean non-alphanumeric chars
    prefix = new string(prefix.Where(char.IsLetterOrDigit).ToArray());

    return $"{prefix}{originalName}";
  }

  /// <summary>
  /// Update $ref references in a document's paths when a schema has been renamed due to conflict
  /// </summary>
  private static void UpdateSchemaReferences(OpenApiDocument doc, string oldName, string newName)
  {
    if (doc.Paths == null) return;

    string oldRef = $"#/components/schemas/{oldName}";
    string newRef = $"#/components/schemas/{newName}";

    foreach (var pathItem in doc.Paths.Values)
    {
      foreach (var operation in pathItem.Operations.Values)
      {
        // Update request body refs
        if (operation.RequestBody?.Content != null)
        {
          foreach (var content in operation.RequestBody.Content.Values)
          {
            if (content.Schema?.Reference?.Id == oldName)
            {
              content.Schema.Reference = new OpenApiReference
              {
                Type = ReferenceType.Schema,
                Id = newName,
              };
            }
          }
        }

        // Update response refs
        foreach (var response in operation.Responses.Values)
        {
          if (response.Content != null)
          {
            foreach (var content in response.Content.Values)
            {
              if (content.Schema?.Reference?.Id == oldName)
              {
                content.Schema.Reference = new OpenApiReference
                {
                  Type = ReferenceType.Schema,
                  Id = newName,
                };
              }
            }
          }
        }
      }
    }
  }
}
