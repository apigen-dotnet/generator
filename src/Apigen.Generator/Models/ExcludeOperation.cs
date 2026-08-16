using System.Text.RegularExpressions;

namespace Apigen.Generator.Models;

/// <summary>
/// Keeps an operation out of the generated client. Meant for endpoints that duplicate another
/// one: vaultwarden exposes <c>POST /folders/{id}</c> alongside <c>PUT /folders/{id}</c> for
/// proxies that block PUT, which lands in the client as a meaningless second method.
///
/// Excluding is deliberately explicit — the generator does not guess which endpoint is
/// redundant. Verify a caller can still reach the functionality some other way before adding
/// one of these.
/// </summary>
public class ExcludeOperation
{
  /// <summary>
  /// Regex matched against the operationId from the spec.
  /// </summary>
  public string? OperationIdFilter { get; set; }

  /// <summary>
  /// Regex matched against the path.
  /// </summary>
  public string? PathFilter { get; set; }

  /// <summary>
  /// Regex matched against the HTTP method (GET, POST, ...).
  /// </summary>
  public string? MethodFilter { get; set; }

  /// <summary>
  /// Why this operation is excluded. Shown when the generator reports what it skipped.
  /// </summary>
  public string Reason { get; set; } = string.Empty;

  /// <summary>
  /// An entry with no filter at all matches nothing: excluding everything by accident is worse
  /// than a config entry that does nothing.
  /// </summary>
  public bool Matches(string? operationId, string path, string method)
  {
    if (string.IsNullOrEmpty(OperationIdFilter) &&
        string.IsNullOrEmpty(PathFilter) &&
        string.IsNullOrEmpty(MethodFilter))
    {
      return false;
    }

    return MatchesPattern(operationId ?? string.Empty, OperationIdFilter)
           && MatchesPattern(path, PathFilter)
           && MatchesPattern(method, MethodFilter);
  }

  private static bool MatchesPattern(string input, string? pattern)
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
