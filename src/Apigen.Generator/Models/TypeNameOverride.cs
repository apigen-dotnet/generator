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

  /// <summary>
  /// Where the override applies: <c>schemas</c> (model type names), <c>resources</c> (the
  /// resource clients and their properties on the main client) or <c>both</c> (default).
  ///
  /// Both are needed in practice — "Oauth" to "OAuth" is meant for the resource client, while
  /// renaming a schema that happens to share its name with a tag (paperless-ngx has both a
  /// "Tasks" schema and a "tasks" tag) must not rename <c>client.Tasks</c> along with it.
  /// </summary>
  public string AppliesTo { get; set; } = "both";

  public bool AppliesToSchemas => !string.Equals(AppliesTo, "resources", StringComparison.OrdinalIgnoreCase);

  public bool AppliesToResources => !string.Equals(AppliesTo, "schemas", StringComparison.OrdinalIgnoreCase);

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