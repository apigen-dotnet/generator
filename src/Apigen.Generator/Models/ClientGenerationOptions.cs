namespace Apigen.Generator.Models;

/// <summary>
/// Configuration options for API client generation
/// </summary>
public class ClientGenerationOptions
{
  /// <summary>
  /// Whether to generate the API client
  /// </summary>
  public bool GenerateClient { get; set; } = false;

  /// <summary>
  /// Namespace for the generated client
  /// </summary>
  public string Namespace { get; set; } = "GeneratedApi.Client";

  /// <summary>
  /// Namespace for the generated models (used in using statements)
  /// </summary>
  public string ModelsNamespace { get; set; } = "GeneratedApi.Models";

  /// <summary>
  /// Project name for the generated client
  /// </summary>
  public string ProjectName { get; set; } = "GeneratedApi.Client";

  /// <summary>
  /// Name of the main client class
  /// </summary>
  public string ClientClassName { get; set; } = "ApiClient";

  /// <summary>
  /// Whether to append "Async" suffix to async methods
  /// </summary>
  public bool UseAsyncSuffix { get; set; } = true;

  /// <summary>
  /// Whether to generate interfaces for all clients
  /// </summary>
  public bool GenerateInterfaces { get; set; } = true;

  /// <summary>
  /// Whether to enable nullable reference types in generated code
  /// </summary>
  public bool GenerateNullableReferenceTypes { get; set; } = true;

  /// <summary>
  /// Authentication configuration for default constructor
  /// </summary>
  public AuthenticationOptions Authentication { get; set; } = new();

  /// <summary>
  /// Response type overrides for operations
  /// </summary>
  public List<ResponseTypeOverride> ResponseTypeOverrides { get; set; } = new();

  /// <summary>
  /// Whether to use ILogger for request/response logging (optional)
  /// </summary>
  public bool UseILogger { get; set; } = false;

  /// <summary>
  /// Request class organization configuration
  /// </summary>
  public RequestOrganizationOptions RequestOrganization { get; set; } = new();
}

/// <summary>
/// Authentication configuration for the API client
/// </summary>
public class AuthenticationOptions
{
  /// <summary>
  /// Header name for the authentication token
  /// </summary>
  public string TokenHeader { get; set; } = "Authorization";

  /// <summary>
  /// Additional required headers
  /// </summary>
  public List<string> RequiredHeaders { get; set; } = new();
}

/// <summary>
/// Configuration for how request classes are organized in the generated client
/// </summary>
public class RequestOrganizationOptions
{
  /// <summary>
  /// Strategy for organizing request classes
  /// Options: "single_file", "individual_files", "by_resource"
  /// </summary>
  public string Strategy { get; set; } = "single_file";

  /// <summary>
  /// Directory name for request files (when not using single_file strategy)
  /// </summary>
  public string Directory { get; set; } = "Requests";

  /// <summary>
  /// Whether to include base classes (ApiResponse, BaseRequest) in shared file
  /// </summary>
  public bool IncludeBaseClasses { get; set; } = true;
}