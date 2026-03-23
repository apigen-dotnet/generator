namespace Apigen.Generator.Models;

/// <summary>
/// Analysis results from parsing an OpenAPI specification
/// </summary>
public class OpenApiAnalysis
{
  /// <summary>
  /// The base URL for the API
  /// </summary>
  public string BaseUrl { get; set; } = string.Empty;

  /// <summary>
  /// Authentication scheme detected from the spec (primary/default)
  /// </summary>
  public AuthenticationScheme Authentication { get; set; } = new();

  /// <summary>
  /// All authentication schemes available in the spec
  /// </summary>
  public List<AuthenticationScheme> AuthenticationSchemes { get; set; } = new();

  /// <summary>
  /// All resource operations discovered
  /// </summary>
  public List<ResourceOperation> Resources { get; set; } = new();

  /// <summary>
  /// Response patterns detected in the API
  /// </summary>
  public ResponsePattern ResponsePattern { get; set; } = new();

  /// <summary>
  /// Common parameter patterns
  /// </summary>
  public ParameterPattern ParameterPattern { get; set; } = new();
}

/// <summary>
/// Authentication scheme detected from OpenAPI security schemes
/// </summary>
public class AuthenticationScheme
{
  public string Name { get; set; } = string.Empty; // Scheme name from spec (e.g., "JWTKeyAuth", "BasicAuth")
  public string Type { get; set; } = "apiKey"; // apiKey, http, basic, oauth2
  public string? HeaderName { get; set; }
  public string? Scheme { get; set; } // for http scheme (bearer, basic, etc.)
  public List<string> RequiredHeaders { get; set; } = new();
}

/// <summary>
/// A resource (like "clients", "invoices") with its operations
/// </summary>
public class ResourceOperation
{
  public string Name { get; set; } = string.Empty;
  public string Tag { get; set; } = string.Empty;
  public List<ApiOperation> Operations { get; set; } = new();
  public string? ModelName { get; set; } // Associated model (Client, Invoice, etc.)
}

/// <summary>
/// Individual API operation (GET, POST, etc.)
/// </summary>
public class ApiOperation
{
  public string Method { get; set; } = string.Empty;
  public string Path { get; set; } = string.Empty;
  public string OperationId { get; set; } = string.Empty;
  public string Summary { get; set; } = string.Empty;
  public OperationType Type { get; set; }
  public List<ApiParameter> Parameters { get; set; } = new();
  public string? RequestBodyType { get; set; }
  public string? ResponseType { get; set; }
  public bool ReturnsWrappedResponse { get; set; } = true; // Most endpoints return wrapped responses
}

/// <summary>
/// Types of operations we can detect
/// </summary>
public enum OperationType
{
  List, // GET /resources
  GetById, // GET /resources/{id}
  Create, // POST /resources
  Update, // PUT /resources/{id}
  Delete, // DELETE /resources/{id}
  Bulk, // POST /resources/bulk
  Custom, // Other operations
}

/// <summary>
/// API parameter definition
/// </summary>
public class ApiParameter
{
  public string Name { get; set; } = string.Empty;
  public string Type { get; set; } = string.Empty;
  public string Location { get; set; } = string.Empty; // query, path, header
  public bool Required { get; set; }
  public string? Description { get; set; }
}

/// <summary>
/// Response patterns detected in the API
/// </summary>
public class ResponsePattern
{
  public bool IsWrapped { get; set; } // true if responses are wrapped in {data: [], meta: {}}
  public string? DataProperty { get; set; } = "data";
  public string? MetaProperty { get; set; } = "meta";
  public bool HasPagination { get; set; }
  public Dictionary<string, string> MetaProperties { get; set; } = new(); // property name -> type

  public Dictionary<string, Dictionary<string, string>> ReferencedSchemas { get; set; } =
    new(); // schema name -> properties
}

/// <summary>
/// Parameter patterns for query string handling
/// </summary>
public class ParameterPattern
{
  public string ArrayStyle { get; set; } = "comma"; // comma, multi, brackets
  public bool SupportsFiltering { get; set; }
  public bool SupportsSorting { get; set; }
  public bool SupportsPagination { get; set; }
  public List<string> PaginationParameters { get; set; } = new();
}