using System.Text.RegularExpressions;

namespace Apigen.Generator.Models;

public class TypeNameOverride
{
  /// <summary>
  /// The original type name from the OpenAPI specification (exact match)
  /// Use this for simple 1:1 replacements
  /// </summary>
  public string? OriginalName { get; set; }

  /// <summary>
  /// Regex pattern to match type names (pattern-based replacement)
  /// Use this for bulk transformations like removing prefixes
  /// Example: "^models\\.(.+)$" matches "models.Permission"
  /// </summary>
  public string? Pattern { get; set; }

  /// <summary>
  /// The new type name to use in generated code
  /// For pattern-based overrides, can use regex groups like "$1"
  /// </summary>
  public string NewName { get; set; } = string.Empty;

  /// <summary>
  /// Optional: Reason for the override (for documentation purposes)
  /// </summary>
  public string? Reason { get; set; }

  private Regex? _compiledPattern;

  /// <summary>
  /// Check if this override matches the given type name
  /// </summary>
  public bool Matches(string typeName)
  {
    if (!string.IsNullOrEmpty(OriginalName))
    {
      return string.Equals(typeName, OriginalName, StringComparison.OrdinalIgnoreCase);
    }

    if (!string.IsNullOrEmpty(Pattern))
    {
      _compiledPattern ??= new Regex(Pattern, RegexOptions.Compiled);
      return _compiledPattern.IsMatch(typeName);
    }

    return false;
  }

  /// <summary>
  /// Apply this override to the given type name
  /// </summary>
  public string Apply(string typeName)
  {
    if (!string.IsNullOrEmpty(Pattern))
    {
      _compiledPattern ??= new Regex(Pattern, RegexOptions.Compiled);
      return _compiledPattern.Replace(typeName, NewName);
    }

    return NewName;
  }
}