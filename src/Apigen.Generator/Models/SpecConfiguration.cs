namespace Apigen.Generator.Models;

/// <summary>
/// Configuration for a single OpenAPI spec input in a multi-spec project
/// </summary>
public class SpecConfiguration
{
  /// <summary>
  /// Path to the OpenAPI spec file (relative to config file or absolute)
  /// </summary>
  public string Path { get; set; } = string.Empty;

  /// <summary>
  /// Path prefix to prepend to all paths in this spec (e.g., "/api", "/identity").
  /// Empty string means no prefix.
  /// </summary>
  public string PathPrefix { get; set; } = string.Empty;
}
