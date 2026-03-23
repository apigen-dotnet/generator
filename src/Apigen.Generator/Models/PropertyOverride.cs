using System.Text.RegularExpressions;

namespace Apigen.Generator.Models;

public class PropertyOverride
{
  /// <summary>
  /// The property name regex pattern to match against original OpenAPI property names (e.g. ".*_at$", "^google_2fa_secret$")
  /// </summary>
  public string PropertyFilter { get; set; } = string.Empty;

  /// <summary>
  /// The new C# type to use instead of the inferred type
  /// </summary>
  public string TargetType { get; set; } = string.Empty;

  /// <summary>
  /// Optional: Specific model name regex pattern to limit the override to (e.g. ".*Settings$", "^User.*")
  /// </summary>
  public string? ModelFilter { get; set; }

  /// <summary>
  /// Optional: Original OpenAPI data type pattern to match (e.g., "string", "number", "integer", "boolean")
  /// </summary>
  public string? OriginalDataType { get; set; }

  /// <summary>
  /// Optional: Original OpenAPI format pattern to match (e.g., "date-time", "int64", "float")
  /// </summary>
  public string? OriginalFormat { get; set; }

  /// <summary>
  /// Optional: JSON converter to use for this property type
  /// </summary>
  public string? JsonConverter { get; set; }

  /// <summary>
  /// Optional: Additional using statements required for this type
  /// </summary>
  public List<string> RequiredUsings { get; set; } = new();

  /// <summary>
  /// Optional: Name of the enum to use for this property (must be defined in enums section)
  /// </summary>
  public string? EnumName { get; set; }

  /// <summary>
  /// Optional: Simple enum name reference (alternative to EnumName for cleaner config)
  /// </summary>
  public string? Enum { get; set; }

  /// <summary>
  /// Check if this override matches the given property, model, and data type
  /// </summary>
  public bool Matches(string propertyName, string modelName, string? dataType = null, string? format = null)
  {
    // Check property pattern
    if (!MatchesPattern(propertyName, PropertyFilter))
    {
      return false;
    }

    // Check model pattern if specified
    if (!string.IsNullOrEmpty(ModelFilter) && !MatchesPattern(modelName, ModelFilter))
    {
      return false;
    }

    // Check original data type if specified
    if (!string.IsNullOrEmpty(OriginalDataType) && !string.IsNullOrEmpty(dataType))
    {
      if (!MatchesPattern(dataType, OriginalDataType))
      {
        return false;
      }
    }

    // Check original format if specified
    if (!string.IsNullOrEmpty(OriginalFormat) && !string.IsNullOrEmpty(format))
    {
      if (!MatchesPattern(format, OriginalFormat))
      {
        return false;
      }
    }

    return true;
  }

  private static bool MatchesPattern(string input, string pattern)
  {
    if (string.IsNullOrEmpty(pattern))
    {
      return true;
    }

    try
    {
      return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
    catch (ArgumentException)
    {
      // If regex is invalid, fall back to exact string comparison
      return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);
    }
  }
}