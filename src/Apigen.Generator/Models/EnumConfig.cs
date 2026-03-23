namespace Apigen.Generator.Models;

/// <summary>
/// Configuration for generating enums
/// </summary>
public class EnumConfig
{
  /// <summary>
  /// The name of the enum to generate
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// Optional description for the enum
  /// </summary>
  public string? Description { get; set; }

  /// <summary>
  /// The enum values and their string representations
  /// </summary>
  public List<EnumValue> Values { get; set; } = new();

  /// <summary>
  /// Alternative: Simple key-value pairs for enum values (parsed from TOML inline table)
  /// Key = JSON string value, Value = C# enum member name
  /// </summary>
  public Dictionary<string, string>? SimpleValues { get; set; }

  /// <summary>
  /// Whether to generate JsonConverter attribute for string enum serialization
  /// </summary>
  public bool GenerateJsonConverter { get; set; } = true;

  /// <summary>
  /// Get all enum values (combines Values and SimpleValues)
  /// </summary>
  public IEnumerable<EnumValue> GetAllValues()
  {
    // Return explicit Values first
    foreach (EnumValue value in Values)
    {
      yield return value;
    }

    // Then convert SimpleValues to EnumValue objects
    if (SimpleValues != null)
    {
      foreach (KeyValuePair<string, string> kvp in SimpleValues)
      {
        yield return new EnumValue
        {
          Name = kvp.Value, // C# enum member name
          Value = kvp.Key, // JSON string value
        };
      }
    }
  }
}

/// <summary>
/// Represents an enum value with its name and string value
/// </summary>
public class EnumValue
{
  /// <summary>
  /// The C# enum member name (e.g., "Always", "Option", "Off")
  /// </summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>
  /// The string value used in JSON/API (e.g., "always", "option", "off")
  /// </summary>
  public string Value { get; set; } = string.Empty;

  /// <summary>
  /// Optional description for this enum value
  /// </summary>
  public string? Description { get; set; }
}