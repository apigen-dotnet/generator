using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace InvoiceNinja.Client;

/// <summary>
/// Extension methods for building query strings
/// </summary>
public static class QueryStringExtensions
{
  /// <summary>
  /// Converts a dictionary of query parameters to a URL-encoded query string
  /// </summary>
  /// <param name="queryParams">Dictionary of query parameters</param>
  /// <returns>URL-encoded query string starting with '?' or empty string if no parameters</returns>
  public static string ToQueryString(this Dictionary<string, object> queryParams)
  {
    if (queryParams.Count == 0) return string.Empty;

    IEnumerable<string> encodedParams = queryParams.Select(kvp =>
      $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value?.ToString())}");

    return "?" + string.Join("&", encodedParams);
  }
}