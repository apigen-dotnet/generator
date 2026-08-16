using System.Text.RegularExpressions;

namespace Apigen.Generator.Models;

/// <summary>
/// Names a type that the spec leaves anonymous: an object schema written inline as a property
/// instead of as a component. Without a name such a property degrades to <c>object</c>, so the
/// generator derives one from the parent schema and property name. This override replaces that
/// derived name, which also keeps it stable when the spec reorders its schemas.
/// </summary>
public class InlineTypeName
{
  /// <summary>
  /// Regex matched against the property name holding the inline object (e.g. "^set_permissions$").
  /// </summary>
  public string PropertyFilter { get; set; } = string.Empty;

  /// <summary>
  /// Optional regex matched against the schema that owns the property. Leave empty to match any
  /// schema — useful because the same inline shape usually repeats across many schemas.
  /// </summary>
  public string? SchemaFilter { get; set; }

  /// <summary>
  /// The C# type name to generate.
  /// </summary>
  public string Name { get; set; } = string.Empty;

  public bool Matches(string propertyName, string schemaName)
  {
    if (!MatchesPattern(propertyName, PropertyFilter))
    {
      return false;
    }

    return string.IsNullOrEmpty(SchemaFilter) || MatchesPattern(schemaName, SchemaFilter);
  }

  private static bool MatchesPattern(string input, string pattern)
  {
    if (string.IsNullOrEmpty(pattern))
    {
      return true;
    }

    try
    {
      return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase);
    }
    catch (ArgumentException)
    {
      return string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase);
    }
  }
}
