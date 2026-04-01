using Microsoft.OpenApi;
using Apigen.Generator.Extensions;

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

    var components = new OpenApiComponents();
    components.Schemas = new Dictionary<string, IOpenApiSchema>();
    components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>();
    var merged = new OpenApiDocument
    {
      Info = new OpenApiInfo
      {
        Title = string.Join(" + ", docs.Select(d => d.Info.Title)),
        Version = docs[0].Info.Version,
      },
      Paths = new OpenApiPaths(),
      Components = components,
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
          var schemaValue = (OpenApiSchema)schema.Value;
          if (!schemaOrigins.ContainsKey(schema.Key))
          {
            // First time seeing this schema name
            schemaOrigins[schema.Key] = (schemaValue, doc.Info.Title);
            merged.Components.Schemas[schema.Key] = schemaValue;
          }
          else
          {
            // Schema name already exists -- check if structurally identical
            if (!AreSchemasStructurallyEqual(schemaOrigins[schema.Key].Schema, schemaValue))
            {
              // Conflict: different structure, same name
              string prefixedName = GenerateConflictName(schema.Key, doc.Info.Title);
              Console.WriteLine($"Warning: Schema '{schema.Key}' differs between specs. Adding as '{prefixedName}' from '{doc.Info.Title}'.");
              merged.Components.Schemas[prefixedName] = schemaValue;

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
        if (((OpenApiSchema)prop.Value).IsNullable() != ((OpenApiSchema)bProp).IsNullable())
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
  /// In OpenApi 3.x, schema references use the Id property instead of a Reference object.
  /// </summary>
  private static void UpdateSchemaReferences(OpenApiDocument doc, string oldName, string newName)
  {
    if (doc.Paths == null) return;

    foreach (var pathItem in doc.Paths.Values)
    {
      foreach (var operation in pathItem.Operations.Values)
      {
        // Update request body refs
        if (operation.RequestBody?.Content != null)
        {
          foreach (var content in operation.RequestBody.Content.Values)
          {
            var schema = (OpenApiSchema?)content.Schema;
            if (schema != null && schema.Id == oldName)
            {
              schema.Id = newName;
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
              var schema = (OpenApiSchema?)content.Schema;
              if (schema != null && schema.Id == oldName)
              {
                schema.Id = newName;
              }
            }
          }
        }
      }
    }
  }
}
