namespace Apigen.Generator.Models;

/// <summary>
/// Operation override for adding fixed parameters and modifying behavior
/// </summary>
public class OperationOverride
{
  /// <summary>
  /// Regex pattern to match against HTTP method (GET, POST, PUT, DELETE, etc.)
  /// </summary>
  public string? HttpMethodFilter { get; set; }

  /// <summary>
  /// Regex pattern to match against the path
  /// </summary>
  public string? PathFilter { get; set; }

  /// <summary>
  /// Content type to match (e.g., "multipart/form-data")
  /// </summary>
  public string? ContentType { get; set; }

  /// <summary>
  /// Parameter name that must exist in the request body
  /// </summary>
  public string? HasBodyParameter { get; set; }

  /// <summary>
  /// Paths to exclude from this override (regex patterns)
  /// </summary>
  public List<string> ExcludePaths { get; set; } = new();

  /// <summary>
  /// Fixed query parameters to add to the operation
  /// </summary>
  public Dictionary<string, string> FixedQueryParameters { get; set; } = new();

  /// <summary>
  /// Description for documentation
  /// </summary>
  public string Description { get; set; } = string.Empty;
}