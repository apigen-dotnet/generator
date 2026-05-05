namespace Apigen.Generator.Models;

/// <summary>
/// Configuration options for naming conventions
/// </summary>
public class NamingOptions
{
  /// <summary>
  /// Specific operationId overrides for problematic names
  /// </summary>
  public Dictionary<string, string> OperationIdOverrides { get; set; } = new();

  /// <summary>
  /// Path-based operation ID overrides for handling duplicate operation IDs
  /// </summary>
  public List<PathBasedOverride> PathBasedOverrides { get; set; } = new();

  /// <summary>
  /// Global name overrides: original spec name -> desired C# name.
  /// Applied BEFORE ToDotNetPascalCase. If a match is found, the override
  /// value is used as-is and ToDotNetPascalCase is skipped.
  /// Works on property names, enum member names, and parameter names.
  /// Keys are matched case-insensitively.
  /// </summary>
  public Dictionary<string, string> Overrides { get; set; } = new();

  /// <summary>
  /// Project-specific acronyms merged on top of the generator's built-in
  /// acronym dictionary (JSON, URL, HTTP, etc.). Use this for domain acronyms
  /// like HAIP, IP, KVK that aren't generally applicable.
  ///
  /// Key is the all-uppercase form as it appears post-PascalCase, value is
  /// the desired normalized form. Project entries override built-ins on key
  /// collision.
  ///
  /// Example: acronyms = { HAIP = "Haip", IP = "Ip" }
  /// </summary>
  public Dictionary<string, string> Acronyms { get; set; } = new();

  /// <summary>
  /// Project-specific stop-words stripped from natural-language operationIds
  /// and spec names *before* PascalCase combines tokens. Use for common
  /// English articles/prepositions that produce awkward identifiers when
  /// preserved (e.g. "Cancel a HA-IP" -> "CancelAHaip").
  ///
  /// Stripping is case-insensitive and only applies to whole tokens (split
  /// on whitespace and hyphens), never to substrings. If stripping would
  /// produce an empty result, the original name is used unchanged.
  ///
  /// Empty by default (no global behavior change).
  ///
  /// Example: stop_words = ["a", "an", "the", "of", "for", "to", "from", "by"]
  /// </summary>
  public List<string> StopWords { get; set; } = new();
}

/// <summary>
/// Path-based operation ID override for handling duplicate operation IDs
/// </summary>
public class PathBasedOverride
{
  /// <summary>
  /// The operation ID to match
  /// </summary>
  public string OperationId { get; set; } = string.Empty;

  /// <summary>
  /// Regex pattern to match against the path
  /// </summary>
  public string PathFilter { get; set; } = string.Empty;

  /// <summary>
  /// The new operation ID to use
  /// </summary>
  public string NewOperationId { get; set; } = string.Empty;

  /// <summary>
  /// Description for documentation
  /// </summary>
  public string Description { get; set; } = string.Empty;
}


/// <summary>
/// Represents an irregular plural fix
/// </summary>
public class IrregularPlural
{
  /// <summary>
  /// The incorrect form to replace
  /// </summary>
  public string From { get; set; } = string.Empty;

  /// <summary>
  /// The correct form to use instead
  /// </summary>
  public string To { get; set; } = string.Empty;
}