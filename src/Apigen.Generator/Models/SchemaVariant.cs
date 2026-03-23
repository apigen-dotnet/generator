using Microsoft.OpenApi.Models;

namespace Apigen.Generator.Models;

/// <summary>
/// Represents a variant of a schema (Request or Response version)
/// </summary>
public class SchemaVariant
{
  public string SchemaName { get; set; } = string.Empty;
  public SchemaVariantType Type { get; set; }
  public OpenApiSchema Schema { get; set; } = null!;

  /// <summary>
  /// Properties included in this variant (after filtering readOnly/writeOnly)
  /// </summary>
  public Dictionary<string, OpenApiSchema> Properties { get; set; } = new();

  /// <summary>
  /// Required properties in this variant
  /// </summary>
  public HashSet<string> Required { get; set; } = new();

  /// <summary>
  /// Nested schema references in this variant
  /// </summary>
  public Dictionary<string, string> NestedReferences { get; set; } = new();

  /// <summary>
  /// Computed hash of the variant structure for comparison
  /// </summary>
  public string StructureHash { get; set; } = string.Empty;
}

public enum SchemaVariantType
{
  Request,
  Response,
  Unified  // When request and response are identical
}
