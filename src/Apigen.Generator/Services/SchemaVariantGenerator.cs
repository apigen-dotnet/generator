using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Apigen.Generator.Models;
using Microsoft.OpenApi;
using Apigen.Generator.Extensions;

namespace Apigen.Generator.Services;

/// <summary>
/// Generates Request and Response variants of schemas by filtering properties
/// </summary>
public class SchemaVariantGenerator
{
  private readonly OpenApiDocument _document;
  private readonly Dictionary<string, SchemaUsage> _usageMap;
  private readonly Dictionary<string, Dictionary<SchemaVariantType, SchemaVariant>> _variants = new();

  public SchemaVariantGenerator(OpenApiDocument document, Dictionary<string, SchemaUsage> usageMap)
  {
    _document = document;
    _usageMap = usageMap;
  }

  /// <summary>
  /// Generates all schema variants
  /// </summary>
  public Dictionary<string, Dictionary<SchemaVariantType, SchemaVariant>> GenerateVariants()
  {
    _variants.Clear();

    if (_document.Components?.Schemas == null)
    {
      return _variants;
    }

    foreach (var schemaEntry in _document.Components.Schemas)
    {
      GenerateVariantsForSchema(schemaEntry.Key, schemaEntry.Value.ResolveSchema());
    }

    return _variants;
  }

  private void GenerateVariantsForSchema(string schemaName, OpenApiSchema schema)
  {
    if (!_usageMap.TryGetValue(schemaName, out SchemaUsage? usage))
    {
      return;
    }

    Dictionary<SchemaVariantType, SchemaVariant> variants = new();

    // Generate request variant if used in requests
    if (usage.IsUsedInRequests)
    {
      SchemaVariant requestVariant = CreateRequestVariant(schemaName, schema);
      variants[SchemaVariantType.Request] = requestVariant;
    }

    // Generate response variant if used in responses
    if (usage.IsUsedInResponses)
    {
      SchemaVariant responseVariant = CreateResponseVariant(schemaName, schema);
      variants[SchemaVariantType.Response] = responseVariant;
    }

    _variants[schemaName] = variants;
  }

  private SchemaVariant CreateRequestVariant(string schemaName, OpenApiSchema schema)
  {
    SchemaVariant variant = new SchemaVariant
    {
      SchemaName = schemaName,
      Type = SchemaVariantType.Request,
      Schema = schema
    };

    // Filter out readOnly properties
    if (schema.Properties != null)
    {
      foreach (var property in schema.Properties)
      {
        if (!property.Value.ReadOnly)
        {
          variant.Properties[property.Key] = property.Value.ResolveSchema();

          // Track nested references
          string? refSchema = GetSchemaName(property.Value);
          if (refSchema != null)
          {
            variant.NestedReferences[property.Key] = refSchema;
          }
        }
      }
    }

    // Filter required properties to only include non-readOnly ones
    if (schema.Required != null)
    {
      foreach (string required in schema.Required)
      {
        if (variant.Properties.ContainsKey(required))
        {
          variant.Required.Add(required);
        }
      }
    }

    // Compute structure hash
    variant.StructureHash = ComputeStructureHash(variant);

    return variant;
  }

  private SchemaVariant CreateResponseVariant(string schemaName, OpenApiSchema schema)
  {
    SchemaVariant variant = new SchemaVariant
    {
      SchemaName = schemaName,
      Type = SchemaVariantType.Response,
      Schema = schema
    };

    // Include all properties except writeOnly
    if (schema.Properties != null)
    {
      foreach (var property in schema.Properties)
      {
        if (!property.Value.WriteOnly)
        {
          variant.Properties[property.Key] = property.Value.ResolveSchema();

          // Track nested references
          string? refSchema = GetSchemaName(property.Value);
          if (refSchema != null)
          {
            variant.NestedReferences[property.Key] = refSchema;
          }
        }
      }
    }

    // Include all non-writeOnly required properties
    if (schema.Required != null)
    {
      foreach (string required in schema.Required)
      {
        if (variant.Properties.ContainsKey(required))
        {
          variant.Required.Add(required);
        }
      }
    }

    // Compute structure hash
    variant.StructureHash = ComputeStructureHash(variant);

    return variant;
  }

  /// <summary>
  /// Computes a hash of the variant structure for comparison
  /// Includes property names, types, required status, and nested references
  /// </summary>
  private string ComputeStructureHash(SchemaVariant variant)
  {
    StringBuilder sb = new StringBuilder();

    // Sort properties for consistent hashing
    foreach (string propName in variant.Properties.Keys.OrderBy(k => k))
    {
      OpenApiSchema propSchema = variant.Properties[propName];

      sb.Append($"{propName}:");
      sb.Append($"{propSchema.Type}:");
      sb.Append($"{propSchema.Format}:");
      sb.Append($"{propSchema.IsNullable()}:");
      sb.Append($"{propSchema.ReadOnly}:");
      sb.Append($"{propSchema.WriteOnly}:");
      sb.Append($"{variant.Required.Contains(propName)}:");
      sb.Append($"{propSchema.MaxLength}:");
      sb.Append($"{propSchema.MinLength}:");
      sb.Append($"{propSchema.Maximum}:");
      sb.Append($"{propSchema.Minimum}:");
      sb.Append($"{propSchema.ExclusiveMaximum}:");
      sb.Append($"{propSchema.ExclusiveMinimum}:");
      sb.Append($"{propSchema.Pattern}:");
      sb.Append($"default:{SerializeOpenApiAny(propSchema.Default)}:");

      // Include enum values - different enums = different schema
      if (propSchema.Enum is { Count: > 0 })
      {
        sb.Append("enum:");
        foreach (var enumVal in propSchema.Enum.OrderBy(e => SerializeOpenApiAny(e)))
        {
          sb.Append($"{SerializeOpenApiAny(enumVal)},");
        }
        sb.Append(':');
      }

      // Include nested reference (which variant it refers to)
      if (variant.NestedReferences.TryGetValue(propName, out string? refSchema))
      {
        sb.Append($"ref:{refSchema}:");

        // Important: Include which VARIANT of the nested schema
        // This ensures Parent splits if Child splits
        if (_usageMap.TryGetValue(refSchema, out SchemaUsage? usage))
        {
          if (usage.HasReadOnlyProperties || usage.HasWriteOnlyProperties)
          {
            sb.Append($"variant:{variant.Type}:");
          }
        }
      }

      // Handle arrays
      if (propSchema.IsType(JsonSchemaType.Array) && propSchema.Items != null)
      {
        string? itemRef = GetSchemaName(propSchema.Items);
        if (itemRef != null)
        {
          sb.Append($"array:{itemRef}:");

          // Include array item variant info
          if (_usageMap.TryGetValue(itemRef, out SchemaUsage? usage))
          {
            if (usage.HasReadOnlyProperties || usage.HasWriteOnlyProperties)
            {
              sb.Append($"variant:{variant.Type}:");
            }
          }
        }
      }

      sb.Append(";");
    }

    // Hash the string
    using SHA256 sha256 = SHA256.Create();
    byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
    byte[] hash = sha256.ComputeHash(bytes);
    return Convert.ToBase64String(hash);
  }

  /// <summary>
  /// Serializes a JsonNode value to a stable string for hashing.
  /// </summary>
  private static string SerializeOpenApiAny(JsonNode? value)
  {
    if (value == null) return "";

    return value.ToJsonString();
  }

  private string? GetSchemaName(IOpenApiSchema? schema)
  {
    if (schema != null && !string.IsNullOrEmpty(schema.Id))
    {
      return schema.Id;
    }

    return null;
  }

  /// <summary>
  /// Gets the variants for a specific schema
  /// </summary>
  public Dictionary<SchemaVariantType, SchemaVariant>? GetVariants(string schemaName)
  {
    return _variants.TryGetValue(schemaName, out Dictionary<SchemaVariantType, SchemaVariant>? variants)
      ? variants
      : null;
  }
}
