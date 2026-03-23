namespace Apigen.Generator.Models;

/// <summary>
/// Configuration for JSON serialization behavior
/// </summary>
public class SerializationOptions
{
  /// <summary>
  /// Whether to make optional value type properties nullable (e.g., int? instead of int)
  /// This prevents sending default values (0, false, etc.) for properties not in the OpenAPI required array
  /// </summary>
  public bool NullableForOptionalProperties { get; set; } = true;

  /// <summary>
  /// JsonIgnoreCondition for serialization
  /// Options: "Never", "WhenWritingNull", "WhenWritingDefault", "Always"
  /// </summary>
  public string IgnoreCondition { get; set; } = "WhenWritingNull";
}
