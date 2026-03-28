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