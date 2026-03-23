namespace Apigen.Generator.Models;

/// <summary>
/// Tracks how a schema is used across the API specification
/// </summary>
public class SchemaUsage
{
  public string SchemaName { get; set; } = string.Empty;

  /// <summary>
  /// Operations where this schema is used as request body
  /// </summary>
  public HashSet<string> UsedInRequestBody { get; set; } = new();

  /// <summary>
  /// Operations where this schema is used as response body
  /// </summary>
  public HashSet<string> UsedInResponse { get; set; } = new();

  /// <summary>
  /// Other schemas that reference this schema
  /// </summary>
  public HashSet<string> ReferencedBy { get; set; } = new();

  /// <summary>
  /// Schemas that this schema references
  /// </summary>
  public HashSet<string> References { get; set; } = new();

  /// <summary>
  /// Whether this schema (or any nested schema) has readOnly properties
  /// </summary>
  public bool HasReadOnlyProperties { get; set; }

  /// <summary>
  /// Whether this schema (or any nested schema) has writeOnly properties
  /// </summary>
  public bool HasWriteOnlyProperties { get; set; }

  /// <summary>
  /// HTTP methods that use this schema in request body (POST, PUT, PATCH, etc.)
  /// </summary>
  public HashSet<string> UsedInRequestMethods { get; set; } = new();

  /// <summary>
  /// HTTP methods that use this schema in response body (GET, POST, etc.)
  /// </summary>
  public HashSet<string> UsedInResponseMethods { get; set; } = new();

  public bool IsUsedInRequests => UsedInRequestBody.Count > 0;
  public bool IsUsedInResponses => UsedInResponse.Count > 0;
  public bool IsUsedInBoth => IsUsedInRequests && IsUsedInResponses;

  // Method-specific usage detection
  public bool IsUsedInPostRequest => UsedInRequestMethods.Contains("POST");
  public bool IsUsedInPutRequest => UsedInRequestMethods.Contains("PUT");
  public bool IsUsedInPatchRequest => UsedInRequestMethods.Contains("PATCH");
  public bool IsUsedInGetResponse => UsedInResponseMethods.Contains("GET");
}
