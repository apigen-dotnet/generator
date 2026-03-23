using System.Text.RegularExpressions;

namespace Apigen.Generator.Models;

public class ResponseTypeOverride
{
  /// <summary>
  /// The operation ID regex pattern to match (e.g. ".*login.*", "^post.*")
  /// </summary>
  public string OperationFilter { get; set; } = string.Empty;

  /// <summary>
  /// The original response type from OpenAPI spec to match
  /// </summary>
  public string OriginalType { get; set; } = string.Empty;

  /// <summary>
  /// The new C# type to use instead of the original type
  /// </summary>
  public string TargetType { get; set; } = string.Empty;

  /// <summary>
  /// Optional: Reason for the override (for documentation)
  /// </summary>
  public string? Reason { get; set; }

  /// <summary>
  /// Check if this override matches the given operation and response type
  /// </summary>
  public bool Matches(string operationId, string responseType)
  {
    // Check operation pattern
    if (!MatchesPattern(operationId, OperationFilter))
    {
      return false;
    }

    // Check original response type
    if (!string.Equals(responseType, OriginalType, StringComparison.OrdinalIgnoreCase))
    {
      return false;
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