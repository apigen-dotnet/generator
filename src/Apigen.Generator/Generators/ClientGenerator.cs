using Microsoft.OpenApi;
using Apigen.Generator.Models;
using Apigen.Generator.Services;
using System.Globalization;
using System.Text;
using StringCasing;
using System.Text.RegularExpressions;
using System.Linq;

namespace Apigen.Generator.Generators;

/// <summary>
/// Generates API client code from OpenAPI specifications
/// </summary>
public class ClientGenerator
{
  private readonly OpenApiAnalyzer _analyzer;
  private readonly TypeMapper _typeMapper;
  private readonly ClientGenerationOptions _options;
  private readonly CodeFormattingOptions _formatting;
  private readonly NamingOptions _naming;
  private readonly SerializationOptions _serialization;
  private readonly List<OperationOverride> _operationOverrides;
  private readonly string _targetFramework;
  private readonly List<TypeNameOverride> _typeNameOverrides;
  private readonly Dictionary<string, ModelGenerationDecision>? _modelDecisions;
  private string _commonPathPrefix = "";

  public ClientGenerator(
    ClientGenerationOptions options,
    CodeFormattingOptions formatting,
    NamingOptions? naming = null,
    List<OperationOverride>? operationOverrides = null,
    string targetFramework = "net8.0",
    List<TypeNameOverride>? typeNameOverrides = null,
    Dictionary<string, ModelGenerationDecision>? modelDecisions = null,
    SerializationOptions? serialization = null)
  {
    _typeNameOverrides = typeNameOverrides ?? new List<TypeNameOverride>();
    _naming = naming ?? new NamingOptions();
    _analyzer = new OpenApiAnalyzer(_typeNameOverrides, _naming.Overrides);
    _typeMapper = new TypeMapper(_typeNameOverrides, _naming.Overrides);
    _options = options;
    _formatting = formatting;
    _serialization = serialization ?? new SerializationOptions();
    _operationOverrides = operationOverrides ?? new List<OperationOverride>();
    _targetFramework = targetFramework;
    _modelDecisions = modelDecisions;
  }

  /// <summary>
  /// Generate API client code from OpenAPI document
  /// </summary>
  public async Task<GeneratedClientCode> GenerateAsync(OpenApiDocument document, string outputPath)
  {
    OpenApiAnalysis analysis = _analyzer.Analyze(document);

    // Detect common path prefix from all paths in the document
    _commonPathPrefix = DetectCommonPathPrefix(document);

    GeneratedClientCode result = new();

    // Generate main client class
    result.MainClient = GenerateMainClient(analysis);

    // Generate resource clients (deduplicated by PascalCase name)
    foreach (ResourceOperation resource in analysis.Resources.DistinctBy(r => r.Name.ToDotNetPascalCase()))
    {
      GeneratedFile resourceClient = GenerateResourceClient(resource, analysis);
      result.ResourceClients.Add(resourceClient);

      // Generate interface if requested
      if (_options.GenerateInterfaces)
      {
        GeneratedFile resourceInterface = GenerateResourceInterface(resource, analysis);
        result.ResourceInterfaces.Add(resourceInterface);
      }
    }

    // Generate request files based on organization strategy
    result.RequestFiles = GenerateRequestFiles(analysis);

    // Generate utility extensions
    GeneratedFile queryStringExtensions = GenerateQueryStringExtensions();
    result.RequestFiles.Add(queryStringExtensions);

    // Generate JSON configuration (shared by all clients)
    GeneratedFile jsonConfig = GenerateJsonConfig();
    result.RequestFiles.Add(jsonConfig);

    // Generate SmartEnumConverter (required by JsonConfig)
    GeneratedFile smartEnumConverter = GenerateSmartEnumConverter();
    result.RequestFiles.Add(smartEnumConverter);

    // Generate multipart content helper if any operations use multipart/form-data
    bool hasMultipartOps = analysis.Resources
      .SelectMany(r => r.Operations)
      .Any(o => o.RequestContentType == "multipart/form-data");
    if (hasMultipartOps)
    {
      GeneratedFile multipartHelper = GenerateMultipartContentExtensions();
      result.RequestFiles.Add(multipartHelper);
    }

    // Generate form-urlencoded content helper if any operations use it
    bool hasFormUrlEncodedOps = analysis.Resources
      .SelectMany(r => r.Operations)
      .Any(o => o.RequestContentType == "application/x-www-form-urlencoded");
    if (hasFormUrlEncodedOps)
    {
      GeneratedFile formHelper = GenerateFormUrlEncodedContentExtensions();
      result.RequestFiles.Add(formHelper);
    }

    // Generate HTTP client logging (if ILogger is enabled)
    if (_options.UseILogger)
    {
      GeneratedFile httpClientLog = GenerateHttpClientLog();
      result.RequestFiles.Add(httpClientLog);
    }

    // Generate project file
    result.ProjectFile = GenerateProjectFile();

    // Write files to disk
    await WriteClientFilesAsync(result, outputPath);

    return result;
  }

  private GeneratedFile GenerateMainClient(OpenApiAnalysis analysis)
  {
    // Get unique resources to avoid duplicates from multiple tags mapping to same PascalCase name
    List<ResourceOperation> uniqueResources = analysis.Resources.DistinctBy(r => r.Name.ToDotNetPascalCase()).ToList();
    return GenerateMainClientInternal(uniqueResources, analysis);
  }

  private GeneratedFile GenerateMainClientInternal(List<ResourceOperation> uniqueResources, OpenApiAnalysis analysis)
  {
    StringBuilder sb = new();
    string indent = _formatting.UseSpaces ? new string(' ', _formatting.IndentWidth) : "\t";

    // File header
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Net.Http;");
    sb.AppendLine("using System.Text.Json;");
    sb.AppendLine("using System.Text.Json.Serialization;");
    sb.AppendLine($"using {_options.ModelsNamespace};");
    if (_options.UseILogger)
    {
      sb.AppendLine("using Microsoft.Extensions.Logging;");
    }

    sb.AppendLine();

    // Add nullable enable directive for auto-generated code when nullable is enabled
    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    // Main client class
    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Main API client for accessing all resources");
    sb.AppendLine("/// </summary>");
    sb.AppendLine($"public class {_options.ClientClassName}");
    sb.AppendLine("{");

    // Fields
    sb.AppendLine($"{indent}private readonly HttpClient _httpClient;");
    sb.AppendLine($"{indent}private readonly bool _disposeHttpClient;");
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}private readonly ILogger? _logger;");
    }

    sb.AppendLine();

    // Resource client properties
    foreach (ResourceOperation resource in uniqueResources)
    {
      string clientName = GetResourceClientName(resource.Name);
      string propertyName = SanitizeOperationId(resource.Name).ToDotNetPascalCase();
      sb.AppendLine($"{indent}/// <summary>");
      sb.AppendLine($"{indent}/// Client for {resource.Name} operations");
      sb.AppendLine($"{indent}/// </summary>");
      sb.AppendLine($"{indent}public {clientName} {propertyName} {{ get; }}");
      sb.AppendLine();
    }

    // Constructor 1: Accept configured HttpClient
    sb.AppendLine($"{indent}/// <summary>");
    sb.AppendLine($"{indent}/// Initialize client with a pre-configured HttpClient");
    sb.AppendLine($"{indent}/// </summary>");
    sb.AppendLine(
      $"{indent}/// <param name=\"httpClient\">Pre-configured HttpClient with base address, auth headers, etc.</param>");
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}/// <param name=\"logger\">Optional logger for request/response logging</param>");
      sb.AppendLine($"{indent}public {_options.ClientClassName}(HttpClient httpClient, ILogger? logger = null)");
    }
    else
    {
      sb.AppendLine($"{indent}public {_options.ClientClassName}(HttpClient httpClient)");
    }

    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}_httpClient = httpClient;");
    sb.AppendLine($"{indent}{indent}_disposeHttpClient = false;");
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}{indent}_logger = logger;");
    }

    sb.AppendLine();

    // Initialize resource clients
    foreach (ResourceOperation resource in uniqueResources)
    {
      string clientName = GetResourceClientName(resource.Name);
      string propertyName = SanitizeOperationId(resource.Name).ToDotNetPascalCase();
      if (_options.UseILogger)
      {
        sb.AppendLine($"{indent}{indent}{propertyName} = new {clientName}(_httpClient, _logger);");
      }
      else
      {
        sb.AppendLine($"{indent}{indent}{propertyName} = new {clientName}(_httpClient);");
      }
    }

    sb.AppendLine($"{indent}}}");
    sb.AppendLine();

    // Private constructor for factory methods (takes ownership of HttpClient)
    sb.AppendLine($"{indent}private {_options.ClientClassName}(HttpClient httpClient, bool disposeHttpClient{(_options.UseILogger ? ", ILogger? logger" : "")})");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}_httpClient = httpClient;");
    sb.AppendLine($"{indent}{indent}_disposeHttpClient = disposeHttpClient;");
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}{indent}_logger = logger;");
    }
    sb.AppendLine();
    foreach (ResourceOperation resource in uniqueResources)
    {
      string clientName = GetResourceClientName(resource.Name);
      string propertyName = SanitizeOperationId(resource.Name).ToDotNetPascalCase();
      if (_options.UseILogger)
      {
        sb.AppendLine($"{indent}{indent}{propertyName} = new {clientName}(_httpClient, _logger);");
      }
      else
      {
        sb.AppendLine($"{indent}{indent}{propertyName} = new {clientName}(_httpClient);");
      }
    }
    sb.AppendLine($"{indent}}}");
    sb.AppendLine();

    // Static factory methods for each authentication scheme
    HashSet<string> generatedFactories = new();
    foreach (AuthenticationScheme authScheme in analysis.AuthenticationSchemes)
    {
      string factoryKey = authScheme.Scheme == HttpAuthScheme.Basic
        ? "basic"
        : authScheme.In == AuthSchemeLocation.Cookie
          ? "cookie"
          : authScheme.Scheme == HttpAuthScheme.Bearer
            ? "bearer"
            : $"apikey_{authScheme.HeaderName}";

      if (!generatedFactories.Contains(factoryKey))
      {
        generatedFactories.Add(factoryKey);
        GenerateAuthFactory(sb, indent, analysis, authScheme, uniqueResources);
      }
    }

    // If no auth schemes detected from the spec, skip factory generation
    // (the user can always pass their own HttpClient to the constructor)

    // Token-based authentication HttpClient factory
    sb.AppendLine($"{indent}private static HttpClient CreateTokenAuthHttpClient(string apiToken, string baseUrl, string headerName, bool useBearer)");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}// Ensure baseUrl ends with / for proper Uri combining with relative paths");
    sb.AppendLine($"{indent}{indent}string normalizedBaseUrl = baseUrl.EndsWith(\"/\") ? baseUrl : baseUrl + \"/\";");
    sb.AppendLine($"{indent}{indent}HttpClient client = new() {{ BaseAddress = new Uri(normalizedBaseUrl) }};");
    sb.AppendLine();
    sb.AppendLine($"{indent}{indent}if (useBearer)");
    sb.AppendLine($"{indent}{indent}{{");
    sb.AppendLine($"{indent}{indent}{indent}client.DefaultRequestHeaders.Add(headerName, $\"Bearer {{apiToken}}\");");
    sb.AppendLine($"{indent}{indent}}}");
    sb.AppendLine($"{indent}{indent}else");
    sb.AppendLine($"{indent}{indent}{{");
    sb.AppendLine($"{indent}{indent}{indent}client.DefaultRequestHeaders.Add(headerName, apiToken);");
    sb.AppendLine($"{indent}{indent}}}");
    sb.AppendLine();

    foreach (string header in _options.Authentication.RequiredHeaders)
    {
      sb.AppendLine($"{indent}{indent}client.DefaultRequestHeaders.Add(\"{header}\", \"XMLHttpRequest\");");
    }

    sb.AppendLine($"{indent}{indent}return client;");
    sb.AppendLine($"{indent}}}");
    sb.AppendLine();

    // Basic authentication HttpClient factory
    sb.AppendLine($"{indent}private static HttpClient CreateBasicAuthHttpClient(string username, string password, string baseUrl)");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}// Ensure baseUrl ends with / for proper Uri combining with relative paths");
    sb.AppendLine($"{indent}{indent}string normalizedBaseUrl = baseUrl.EndsWith(\"/\") ? baseUrl : baseUrl + \"/\";");
    sb.AppendLine($"{indent}{indent}HttpClient client = new() {{ BaseAddress = new Uri(normalizedBaseUrl) }};");
    sb.AppendLine();
    sb.AppendLine($"{indent}{indent}string credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($\"{{username}}:{{password}}\"));");
    sb.AppendLine($"{indent}{indent}client.DefaultRequestHeaders.Add(\"Authorization\", $\"Basic {{credentials}}\");");
    sb.AppendLine();

    foreach (string header in _options.Authentication.RequiredHeaders)
    {
      sb.AppendLine($"{indent}{indent}client.DefaultRequestHeaders.Add(\"{header}\", \"XMLHttpRequest\");");
    }

    sb.AppendLine($"{indent}{indent}return client;");
    sb.AppendLine($"{indent}}}");
    sb.AppendLine();

    // Cookie-based authentication HttpClient factory
    sb.AppendLine($"{indent}private static HttpClient CreateCookieAuthHttpClient(string token, string cookieName, string baseUrl)");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}string normalizedBaseUrl = baseUrl.EndsWith(\"/\") ? baseUrl : baseUrl + \"/\";");
    sb.AppendLine($"{indent}{indent}System.Net.CookieContainer cookies = new();");
    sb.AppendLine($"{indent}{indent}cookies.Add(new Uri(normalizedBaseUrl), new System.Net.Cookie(cookieName, token));");
    sb.AppendLine($"{indent}{indent}HttpClientHandler handler = new() {{ CookieContainer = cookies }};");
    sb.AppendLine($"{indent}{indent}HttpClient client = new(handler) {{ BaseAddress = new Uri(normalizedBaseUrl) }};");
    sb.AppendLine();

    foreach (string header in _options.Authentication.RequiredHeaders)
    {
      sb.AppendLine($"{indent}{indent}client.DefaultRequestHeaders.Add(\"{header}\", \"XMLHttpRequest\");");
    }

    sb.AppendLine($"{indent}{indent}return client;");
    sb.AppendLine($"{indent}}}");
    sb.AppendLine();

    // Dispose method
    sb.AppendLine($"{indent}/// <summary>");
    sb.AppendLine($"{indent}/// Dispose resources");
    sb.AppendLine($"{indent}/// </summary>");
    sb.AppendLine($"{indent}public void Dispose()");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}if (_disposeHttpClient)");
    sb.AppendLine($"{indent}{indent}{{");
    sb.AppendLine($"{indent}{indent}{indent}_httpClient?.Dispose();");
    sb.AppendLine($"{indent}{indent}}}");
    sb.AppendLine($"{indent}}}");

    sb.AppendLine("}");

    return new GeneratedFile
    {
      FileName = $"{_options.ClientClassName}.cs",
      Content = sb.ToString(),
    };
  }

  private GeneratedFile GenerateResourceClient(ResourceOperation resource, OpenApiAnalysis analysis)
  {
    StringBuilder sb = new();
    string indent = _formatting.UseSpaces ? new string(' ', _formatting.IndentWidth) : "\t";
    string clientName = GetResourceClientName(resource.Name);

    // File header
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Net.Http;");
    sb.AppendLine("using System.Text;");
    sb.AppendLine("using System.Text.Json;");
    sb.AppendLine("using System.Text.Json.Serialization;");
    sb.AppendLine("using System.Threading.Tasks;");
    sb.AppendLine($"using {_options.ModelsNamespace};");
    if (_options.UseILogger)
    {
      sb.AppendLine("using Microsoft.Extensions.Logging;");
    }

    sb.AppendLine();

    // Add nullable enable directive for auto-generated code when nullable is enabled
    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    // Resource client class
    sb.AppendLine("/// <summary>");
    sb.AppendLine($"/// Client for {resource.Name} operations");
    sb.AppendLine("/// </summary>");
    sb.AppendLine($"public class {clientName}");
    sb.AppendLine("{");

    // HttpClient field
    sb.AppendLine($"{indent}private readonly HttpClient _httpClient;");
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}private readonly ILogger? _logger;");
    }

    sb.AppendLine();

    // Constructor
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}internal {clientName}(HttpClient httpClient, ILogger? logger = null)");
    }
    else
    {
      sb.AppendLine($"{indent}internal {clientName}(HttpClient httpClient)");
    }

    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}_httpClient = httpClient;");
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}{indent}_logger = logger;");
    }

    sb.AppendLine($"{indent}}}");
    sb.AppendLine();

    // Generate methods for each operation, checking for conflicts
    Dictionary<string, ApiOperation> generatedMethods = new();

    foreach (ApiOperation operation in resource.Operations)
    {
      string methodName = GetMethodName(operation, resource.Name);
      string parameters = GenerateMethodParameters(operation);

      // Extract just the parameter types for signature checking (not parameter names)
      string parameterTypes = ExtractParameterTypes(parameters);
      string fullSignature = $"{methodName}({parameterTypes})";

      // Check for conflicts
      if (generatedMethods.ContainsKey(fullSignature))
      {
        // Generate path-based name to avoid conflict
        string pathBasedName = GenerateMethodNameFromPath(operation);
        methodName = _options.UseAsyncSuffix ? $"{pathBasedName}Async" : pathBasedName;
        fullSignature = $"{methodName}({parameterTypes})";
      }

      generatedMethods[fullSignature] = operation;

      string method = GenerateOperationMethod(operation, resource, analysis, indent, methodName);
      sb.AppendLine(method);
      sb.AppendLine();
    }

    sb.AppendLine("}");

    return new GeneratedFile
    {
      FileName = $"{clientName}.cs",
      Content = sb.ToString(),
    };
  }

  private string GenerateOperationMethod(
    ApiOperation operation,
    ResourceOperation resource,
    OpenApiAnalysis analysis,
    string indent,
    string? methodNameOverride = null)
  {
    StringBuilder sb = new();
    string methodName = methodNameOverride ?? GetMethodName(operation, resource.Name);
    string returnType = GetReturnType(operation, analysis);

    // Method signature
    sb.AppendLine($"{indent}/// <summary>");
    sb.AppendLine($"{indent}/// {FormatXmlDocumentation(operation.Summary, indent)}");
    sb.AppendLine($"{indent}/// Operation: {operation.Method.ToUpper()} {operation.Path}");
    sb.AppendLine($"{indent}/// </summary>");

    string parameters = GenerateMethodParameters(operation);
    // For void responses, use Task instead of Task<void>
    string taskReturnType = returnType == "void" ? "Task" : $"Task<{returnType}>";
    sb.AppendLine($"{indent}public async {taskReturnType} {methodName}({parameters})");
    sb.AppendLine($"{indent}{{");

    // Method body
    string body = GenerateMethodBody(operation, resource, analysis, indent + indent);
    sb.Append(body);

    sb.AppendLine($"{indent}}}");

    return sb.ToString();
  }

  private string GetMethodName(ApiOperation operation, string resourceName)
  {
    // Get operationId, fallback to auto-generated if null/empty
    string operationId = operation.OperationId;
    if (string.IsNullOrEmpty(operationId))
    {
      operationId = GenerateOperationId(operation);
    }

    // Sanitize operation ID BEFORE applying overrides
    // This ensures the original spec's operation ID is sanitized, but overrides are respected as-is
    string originalOperationId = operationId;
    operationId = SanitizeOperationId(operationId);

    // Apply operation ID overrides (these should be used as-is without sanitization)
    string overriddenOperationId = ApplyOperationIdOverrides(originalOperationId, operation);
    bool overrideApplied = overriddenOperationId != originalOperationId;
    if (overrideApplied)
    {
      // An override was applied, use it directly without sanitization
      operationId = overriddenOperationId;
    }

    string baseName;

    // Only use operation type-based naming if NO override was applied
    if (!overrideApplied)
    {
      baseName = operation.Type switch
      {
        Models.OperationType.List => "List",
        Models.OperationType.GetById => "Get",
        Models.OperationType.Create => "Create",
        Models.OperationType.Update => "Update",
        Models.OperationType.Delete => "Delete",
        Models.OperationType.Bulk => "Bulk",
        _ => operationId.ToDotNetPascalCase(),
      };

      // If we still have conflicts, use path-based naming as fallback
      if (baseName == "GetinvoicesAsync")
      {
        baseName = GenerateMethodNameFromPath(operation);
      }

      // Strip redundant resource name from method name when it's already in the class name
      // E.g., "Getproject" in ProjectClient becomes "Get"
      baseName = StripRedundantResourceName(baseName, resourceName);
    }
    else
    {
      // Override was applied, use it as the base name (don't strip resource name for overrides)
      baseName = operationId;
    }

    return _options.UseAsyncSuffix ? $"{baseName}Async" : baseName;
  }

  private string StripRedundantResourceName(string methodName, string resourceName)
  {
    // Normalize both to lowercase for comparison
    string normalizedMethod = methodName.ToLowerInvariant();
    string normalizedResource = resourceName.ToLowerInvariant().TrimEnd('s'); // "projects" -> "project"

    // Common HTTP method prefixes
    string[] prefixes = { "get", "post", "put", "delete", "patch", "list", "create", "update" };

    foreach (string prefix in prefixes)
    {
      // Check if method name is like "Getproject" and resource is "project"
      string expectedPattern = prefix + normalizedResource;
      if (normalizedMethod == expectedPattern)
      {
        // Return just the prefix with original casing
        return prefix.ToDotNetPascalCase();
      }
    }

    // No redundancy found, return original
    return methodName;
  }

  private string GenerateOperationId(ApiOperation operation)
  {
    // Generate operation ID from HTTP method and path
    string method = operation.Method.ToLowerInvariant();

    // Extract meaningful parts from path
    string[] pathSegments = operation.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
    List<string> meaningfulSegments = new();

    foreach (string segment in pathSegments)
    {
      // Skip API version segments and parameter segments
      if (!segment.StartsWith("{") &&
          !segment.Equals("api", StringComparison.OrdinalIgnoreCase) &&
          !segment.StartsWith("v", StringComparison.OrdinalIgnoreCase) &&
          !char.IsDigit(segment.FirstOrDefault()))
      {
        meaningfulSegments.Add(segment);
      }
    }

    // Build operation ID: method + joined segments
    string resourcePart = string.Join("", meaningfulSegments.Select(s => s.ToDotNetPascalCase()));
    return method + resourcePart;
  }

  private string ApplyOperationIdOverrides(string operationId, ApiOperation operation)
  {
    // Check for specific operationId override
    if (_naming.OperationIdOverrides.TryGetValue(operationId, out string? overrideId))
    {
      operationId = overrideId;
    }

    // Check for path-based overrides
    foreach (PathBasedOverride pathOverride in _naming.PathBasedOverrides)
    {
      if (pathOverride.OperationId == operationId &&
          Regex.IsMatch(operation.Path, pathOverride.PathFilter))
      {
        operationId = pathOverride.NewOperationId;
        break; // Use first matching override
      }
    }

    return operationId;
  }

  /// <summary>
  /// Sanitizes an operation ID to be a valid C# identifier
  /// Removes parentheses and other special characters
  /// </summary>
  private string SanitizeOperationId(string operationId)
  {
    if (string.IsNullOrEmpty(operationId))
    {
      return operationId;
    }

    // Use TypeMapper's GetClassName to ensure consistent sanitization
    return _typeMapper.GetClassName(operationId);
  }

  private string GenerateMethodNameFromPath(ApiOperation operation)
  {
    // Use path segments to create unique method names
    string[] pathSegments = operation.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)
      .Where(s => !s.StartsWith("{") && s != "api" && s != "v1")
      .ToArray();

    if (pathSegments.Length == 0)
    {
      return operation.Method.ToUpperInvariant();
    }

    // Normalize HTTP method: "GET" -> "Get", "POST" -> "Post", etc.
    // Use existing ToPascalCase extension after lowercasing
    string methodName = operation.Method.ToLowerInvariant().ToDotNetPascalCase();

    // Build method name from path segments with proper normalization
    // Apply the same normalization as we do for operationIds
    string pathName = string.Concat(pathSegments.Select(s => SanitizeOperationId(s).ToDotNetPascalCase()));
    return $"{methodName}{pathName}";
  }

  private string GetReturnType(ApiOperation operation, OpenApiAnalysis analysis)
  {
    string baseType = operation.ResponseType ?? "object";

    // Resolve to canonical schema name if this was deduplicated
    baseType = ResolveCanonicalSchemaName(baseType);

    // Handle void responses (204 No Content or no response body)
    if (baseType == "void")
    {
      return "void";
    }

    // Apply response type overrides from configuration
    foreach (ResponseTypeOverride responseOverride in _options.ResponseTypeOverrides)
    {
      string operationIdForOverride = operation.OperationId ?? GenerateOperationId(operation);
      if (responseOverride.Matches(operationIdForOverride, baseType))
      {
        baseType = responseOverride.TargetType;
        break; // Use first matching override
      }
    }

    // Apply type name overrides from configuration (e.g., TaskStatus -> TaskItemStatus)
    baseType = ApplyTypeNameOverrides(baseType);

    // JsonElement doesn't need qualification - it's from System.Text.Json
    if (baseType != "JsonElement")
    {
      // Fully qualify types to avoid namespace conflicts
      baseType = FullyQualifyModelTypes(baseType);
    }

    if (analysis.ResponsePattern.IsWrapped)
    {
      return $"ApiResponse<{baseType}>";
    }

    return baseType;
  }

  private string GenerateMethodParameters(ApiOperation operation)
  {
    List<string> parameters = new();

    // Add path parameters
    foreach (ApiParameter param in operation.Parameters.Where(p => p.Location == "path"))
    {
      parameters.Add($"{param.Type} {param.Name.ToDotNetCamelCase()}");
    }

    // Add request body parameter
    if (!string.IsNullOrEmpty(operation.RequestBodyType))
    {
      // Resolve to canonical schema name if this was deduplicated
      string resolvedType = ResolveCanonicalSchemaName(operation.RequestBodyType);

      string fullyQualifiedType = resolvedType;

      // Apply type name overrides from configuration
      fullyQualifiedType = ApplyTypeNameOverrides(fullyQualifiedType);

      // Derive parameter name from the (possibly overridden) type name
      string paramName = fullyQualifiedType.ToDotNetCamelCase();

      // Check if this schema was split into Request/Response models
      // If so, use the Request variant for POST/PUT/PATCH operations
      if (_modelDecisions != null && _modelDecisions.TryGetValue(resolvedType, out ModelGenerationDecision? decision))
      {
        if (decision.ShouldSplit)
        {
          // Append "Request" suffix to the already-overridden type name
          fullyQualifiedType = fullyQualifiedType + "Request";
        }
      }

      // Always fully qualify request body types with the models namespace if not already qualified
      if (!fullyQualifiedType.Contains("."))
      {
        fullyQualifiedType = $"{_options.ModelsNamespace}.{fullyQualifiedType}";
      }

      parameters.Add($"{fullyQualifiedType} {paramName}");
    }

    // Add query parameters as optional
    List<ApiParameter> queryParams = operation.Parameters.Where(p => p.Location == "query").ToList();
    if (queryParams.Any())
    {
      string operationId = operation.OperationId;
      if (string.IsNullOrEmpty(operationId))
      {
        operationId = GenerateOperationId(operation);
      }
      string originalOperationId = operationId;
      operationId = SanitizeOperationId(operationId);

      // Apply overrides without sanitization
      string overriddenOperationId = ApplyOperationIdOverrides(originalOperationId, operation);
      if (overriddenOperationId != originalOperationId)
      {
        operationId = overriddenOperationId;
      }

      parameters.Add($"{operationId.ToDotNetPascalCase()}Request? request = null");
    }

    return string.Join(", ", parameters);
  }

  private string GenerateMethodBody(
    ApiOperation operation,
    ResourceOperation resource,
    OpenApiAnalysis analysis,
    string indent)
  {
    StringBuilder sb = new();

    // Build URL
    string urlBuilder = GenerateUrlBuilder(operation, indent);
    sb.Append(urlBuilder);

    // Make HTTP request
    string httpCall = GenerateHttpCall(operation, indent);
    sb.Append(httpCall);

    // Process response
    string responseProcessor = GenerateResponseProcessor(operation, analysis, indent);
    sb.Append(responseProcessor);

    return sb.ToString();
  }

  private string GenerateUrlBuilder(ApiOperation operation, string indent)
  {
    StringBuilder sb = new();

    string pathTemplate = operation.Path;

    // Strip common path prefix if detected (this will automatically handle the leading slash)
    if (!string.IsNullOrEmpty(_commonPathPrefix) && pathTemplate.StartsWith(_commonPathPrefix))
    {
      pathTemplate = pathTemplate.Substring(_commonPathPrefix.Length);
    }

    // Strip leading slash for HttpClient BaseAddress compatibility
    // HttpClient treats paths with leading '/' as absolute from server root, not relative to BaseAddress
    if (pathTemplate.StartsWith("/"))
    {
      pathTemplate = pathTemplate.Substring(1);
    }

    // Build URL using extension method with explicit dictionary for path parameters
    var pathParams = operation.Parameters.Where(p => p.Location == "path").ToList();
    if (pathParams.Any())
    {
      // Generate dictionary for explicit path parameter mapping
      sb.AppendLine($"{indent}Dictionary<string, object> pathParams = new()");
      sb.AppendLine($"{indent}{{");
      for (int i = 0; i < pathParams.Count; i++)
      {
        var param = pathParams[i];
        // Use exact template placeholder name as dictionary key (case-sensitive mapping)
        sb.Append($"{indent}  [\"{param.Name}\"] = {param.Name.ToDotNetCamelCase()}");
        if (i < pathParams.Count - 1)
          sb.Append(",");
        sb.AppendLine();
      }
      sb.AppendLine($"{indent}}};");

      // Build URL with dictionary
      IEnumerable<ApiParameter> queryParams = operation.Parameters.Where(p => p.Location == "query");
      if (queryParams.Any())
      {
        sb.AppendLine($"{indent}string url = \"{pathTemplate}\".BuildUrl(pathParams, request);");
      }
      else
      {
        sb.AppendLine($"{indent}string url = \"{pathTemplate}\".BuildUrl(pathParams);");
      }
    }
    else
    {
      // No path parameters, but might have query parameters
      IEnumerable<ApiParameter> queryParams = operation.Parameters.Where(p => p.Location == "query");
      if (queryParams.Any())
      {
        sb.AppendLine($"{indent}string url = \"{pathTemplate}\".BuildUrl(request: request);");
      }
      else
      {
        sb.AppendLine($"{indent}string url = \"{pathTemplate}\";");
      }
    }

    sb.AppendLine();
    return sb.ToString();
  }

  private string GenerateHttpCall(ApiOperation operation, string indent)
  {
    StringBuilder sb = new();

    // Start timing
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();");
    }

    // Add logging before HTTP call
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}HttpClientLog.LogDebugRequestStarted(_logger, \"{operation.Method.ToUpperInvariant()}\", url);");
    }

    switch (operation.Method.ToUpperInvariant())
    {
      case "GET":
        sb.AppendLine($"{indent}HttpResponseMessage response = await _httpClient.GetAsync(url);");
        break;
      case "POST":
        if (!string.IsNullOrEmpty(operation.RequestBodyType))
        {
          string paramName = ApplyTypeNameOverrides(ResolveCanonicalSchemaName(operation.RequestBodyType)).ToDotNetCamelCase();
          if (operation.RequestContentType == "multipart/form-data")
          {
            sb.AppendLine($"{indent}MultipartFormDataContent content = {paramName}.ToMultipartContent();");
            if (_options.UseILogger)
            {
              sb.AppendLine($"{indent}HttpClientLog.LogTraceRequestBody(_logger, \"POST\", \"multipart/form-data\", \"[binary content]\");");
            }
          }
          else if (operation.RequestContentType == "application/x-www-form-urlencoded")
          {
            sb.AppendLine($"{indent}FormUrlEncodedContent content = {paramName}.ToFormUrlEncodedContent();");
            if (_options.UseILogger)
            {
              sb.AppendLine($"{indent}string formBody = await content.ReadAsStringAsync();");
              sb.AppendLine($"{indent}HttpClientLog.LogTraceRequestBody(_logger, \"POST\", \"application/x-www-form-urlencoded\", formBody);");
            }
          }
          else
          {
            sb.AppendLine($"{indent}string json = JsonSerializer.Serialize({paramName}, JsonConfig.Default);");
            if (_options.UseILogger)
            {
              sb.AppendLine($"{indent}HttpClientLog.LogTraceRequestBody(_logger, \"POST\", \"application/json\", json);");
            }
            sb.AppendLine($"{indent}StringContent content = new StringContent(json, Encoding.UTF8, \"application/json\");");
          }
          sb.AppendLine($"{indent}HttpResponseMessage response = await _httpClient.PostAsync(url, content);");
        }
        else
        {
          sb.AppendLine($"{indent}HttpResponseMessage response = await _httpClient.PostAsync(url, null);");
        }

        break;
      case "PUT":
        if (!string.IsNullOrEmpty(operation.RequestBodyType))
        {
          string paramName = ApplyTypeNameOverrides(ResolveCanonicalSchemaName(operation.RequestBodyType)).ToDotNetCamelCase();
          if (operation.RequestContentType == "multipart/form-data")
          {
            sb.AppendLine($"{indent}MultipartFormDataContent content = {paramName}.ToMultipartContent();");
            if (_options.UseILogger)
            {
              sb.AppendLine($"{indent}HttpClientLog.LogTraceRequestBody(_logger, \"PUT\", \"multipart/form-data\", \"[binary content]\");");
            }
          }
          else if (operation.RequestContentType == "application/x-www-form-urlencoded")
          {
            sb.AppendLine($"{indent}FormUrlEncodedContent content = {paramName}.ToFormUrlEncodedContent();");
            if (_options.UseILogger)
            {
              sb.AppendLine($"{indent}string formBody = await content.ReadAsStringAsync();");
              sb.AppendLine($"{indent}HttpClientLog.LogTraceRequestBody(_logger, \"PUT\", \"application/x-www-form-urlencoded\", formBody);");
            }
          }
          else
          {
            sb.AppendLine($"{indent}string json = JsonSerializer.Serialize({paramName}, JsonConfig.Default);");
            if (_options.UseILogger)
            {
              sb.AppendLine($"{indent}HttpClientLog.LogTraceRequestBody(_logger, \"PUT\", \"application/json\", json);");
            }
            sb.AppendLine($"{indent}StringContent content = new StringContent(json, Encoding.UTF8, \"application/json\");");
          }
          sb.AppendLine($"{indent}HttpResponseMessage response = await _httpClient.PutAsync(url, content);");
        }
        else
        {
          sb.AppendLine($"{indent}HttpResponseMessage response = await _httpClient.PutAsync(url, null);");
        }

        break;
      case "PATCH":
        if (!string.IsNullOrEmpty(operation.RequestBodyType))
        {
          string paramName = ApplyTypeNameOverrides(ResolveCanonicalSchemaName(operation.RequestBodyType)).ToDotNetCamelCase();
          sb.AppendLine($"{indent}string json = JsonSerializer.Serialize({paramName}, JsonConfig.Default);");
          if (_options.UseILogger)
          {
             sb.AppendLine($"{indent}HttpClientLog.LogTraceRequestBody(_logger, \"PATCH\", \"application/json\", json);");
          }

          sb.AppendLine(
            $"{indent}StringContent content = new StringContent(json, Encoding.UTF8, \"application/json\");");
          sb.AppendLine($"{indent}HttpResponseMessage response = await _httpClient.PatchAsync(url, content);");
        }
        else
        {
          sb.AppendLine($"{indent}HttpResponseMessage response = await _httpClient.PatchAsync(url, null);");
        }

        break;
      case "DELETE":
        sb.AppendLine($"{indent}HttpResponseMessage response = await _httpClient.DeleteAsync(url);");
        break;
    }

    // Calculate duration and log completion
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}long durationMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;");
      sb.AppendLine($"{indent}HttpClientLog.LogDebugRequestCompleted(_logger, (int)response.StatusCode, \"{operation.Method.ToUpperInvariant()}\", url, durationMs);");
    }

    sb.AppendLine();
    return sb.ToString();
  }

  private string GenerateResponseProcessor(ApiOperation operation, OpenApiAnalysis analysis, string indent)
  {
    StringBuilder sb = new();

    // Handle binary responses (application/octet-stream, image/*, etc.)
    if (operation.ResponseType == "Stream")
    {
      if (_options.UseILogger)
      {
        sb.AppendLine($"{indent}try");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}  response.EnsureSuccessStatusCode();");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}catch (HttpRequestException ex)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}  string errorContent = await response.Content.ReadAsStringAsync();");
        sb.AppendLine($"{indent}  HttpClientLog.LogErrorRequestFailed(_logger, (int)response.StatusCode, \"{operation.Method.ToUpper()}\", url, errorContent, ex);");
        sb.AppendLine($"{indent}  throw;");
        sb.AppendLine($"{indent}}}");
      }
      else
      {
        sb.AppendLine($"{indent}response.EnsureSuccessStatusCode();");
      }

      sb.AppendLine($"{indent}return await response.Content.ReadAsStreamAsync();");
      return sb.ToString();
    }

    // Handle void responses (204 No Content or empty body)
    if (operation.ResponseType == "void")
    {
      if (_options.UseILogger)
      {
        sb.AppendLine($"{indent}try");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}  response.EnsureSuccessStatusCode();");
        sb.AppendLine($"{indent}}}");
        sb.AppendLine($"{indent}catch (HttpRequestException ex)");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}  string responseContent = await response.Content.ReadAsStringAsync();");
        sb.AppendLine($"{indent}  HttpClientLog.LogErrorRequestFailed(_logger, (int)response.StatusCode, \"{operation.Method.ToUpper()}\", url, responseContent, ex);");
        sb.AppendLine($"{indent}  throw;");
        sb.AppendLine($"{indent}}}");
      }
      else
      {
        sb.AppendLine($"{indent}response.EnsureSuccessStatusCode();");
      }

      return sb.ToString();
    }

    // For all other responses, read the content
    sb.AppendLine($"{indent}string responseContent;");
    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}try");
      sb.AppendLine($"{indent}{{");
      sb.AppendLine($"{indent}  response.EnsureSuccessStatusCode();");
      sb.AppendLine($"{indent}  responseContent = await response.Content.ReadAsStringAsync();");
      sb.AppendLine($"{indent}}}");
      sb.AppendLine($"{indent}catch (HttpRequestException ex)");
      sb.AppendLine($"{indent}{{");
      sb.AppendLine($"{indent}  responseContent = await response.Content.ReadAsStringAsync();");
      sb.AppendLine($"{indent}  HttpClientLog.LogErrorRequestFailed(_logger, (int)response.StatusCode, \"{operation.Method.ToUpper()}\", url, responseContent, ex);");
      sb.AppendLine($"{indent}  throw;");
      sb.AppendLine($"{indent}}}");
    }
    else
    {
      sb.AppendLine($"{indent}response.EnsureSuccessStatusCode();");
      sb.AppendLine($"{indent}responseContent = await response.Content.ReadAsStringAsync();");
    }    sb.AppendLine();

    if (_options.UseILogger)
    {
      sb.AppendLine($"{indent}HttpClientLog.LogTraceResponseBody(_logger, url, responseContent);");
    }

    if (operation.ResponseType != null)
    {
      // Resolve to canonical schema name if this was deduplicated
      string? fullyQualifiedResponseType = ResolveCanonicalSchemaName(operation.ResponseType);

      // Apply response type overrides from configuration (same logic as GetReturnType)
      foreach (ResponseTypeOverride responseOverride in _options.ResponseTypeOverrides)
      {
        string operationIdForOverride = operation.OperationId ?? GenerateOperationId(operation);
        if (responseOverride.Matches(operationIdForOverride, fullyQualifiedResponseType))
        {
          fullyQualifiedResponseType = responseOverride.TargetType;
          break; // Use first matching override
        }
      }

      // Apply type name overrides from configuration (e.g., TaskStatus -> TaskItemStatus)
      fullyQualifiedResponseType = ApplyTypeNameOverrides(fullyQualifiedResponseType);

      // Handle JsonElement responses (unknown schema with JSON content)
      if (fullyQualifiedResponseType == "JsonElement")
      {
        if (analysis.ResponsePattern.IsWrapped)
        {
          sb.AppendLine($"{indent}ApiResponse<JsonElement>? apiResponse = JsonSerializer.Deserialize<ApiResponse<JsonElement>>(responseContent, JsonConfig.Default);");
          sb.AppendLine($"{indent}return apiResponse ?? new ApiResponse<JsonElement>();");
        }
        else
        {
          sb.AppendLine($"{indent}JsonElement result = JsonSerializer.Deserialize<JsonElement>(responseContent, JsonConfig.Default);");
          sb.AppendLine($"{indent}return result;");
        }
        return sb.ToString();
      }

      // Fully qualify types to avoid namespace conflicts
      fullyQualifiedResponseType = FullyQualifyModelTypes(fullyQualifiedResponseType);

      if (analysis.ResponsePattern.IsWrapped)
      {
        sb.AppendLine(
          $"{indent}ApiResponse<{fullyQualifiedResponseType}>? apiResponse = JsonSerializer.Deserialize<ApiResponse<{fullyQualifiedResponseType}>>(responseContent, JsonConfig.Default);");
        sb.AppendLine($"{indent}return apiResponse ?? new ApiResponse<{fullyQualifiedResponseType}>();");
      }
      else
      {
        sb.AppendLine(
          $"{indent}{fullyQualifiedResponseType}? result = JsonSerializer.Deserialize<{fullyQualifiedResponseType}>(responseContent, JsonConfig.Default);");
        sb.AppendLine($"{indent}return result ?? new {fullyQualifiedResponseType}();");
      }
    }
    else
    {
      // Empty response - just return new object()
      sb.AppendLine($"{indent}return new object();");
    }

    return sb.ToString();
  }

  private GeneratedFile GenerateResourceInterface(ResourceOperation resource, OpenApiAnalysis analysis)
  {
    StringBuilder sb = new();
    string indent = _formatting.UseSpaces ? new string(' ', _formatting.IndentWidth) : "\t";
    string interfaceName = GetResourceInterfaceName(resource.Name);

    // File header
    sb.AppendLine("using System.Text.Json;");
    sb.AppendLine("using System.Threading.Tasks;");
    sb.AppendLine($"using {_options.ModelsNamespace};");
    sb.AppendLine();

    // Add nullable enable directive for auto-generated code when nullable is enabled
    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    sb.AppendLine("/// <summary>");
    sb.AppendLine($"/// Interface for {resource.Name} operations");
    sb.AppendLine("/// </summary>");
    sb.AppendLine($"public interface {interfaceName}");
    sb.AppendLine("{");

    Dictionary<string, ApiOperation> generatedMethods = new();

    foreach (ApiOperation operation in resource.Operations)
    {
      string methodName = GetMethodName(operation, resource.Name);
      string returnType = GetReturnType(operation, analysis);
      string parameters = GenerateMethodParameters(operation);

      // Extract just the parameter types for signature checking (not parameter names)
      string parameterTypes = ExtractParameterTypes(parameters);
      string fullSignature = $"{methodName}({parameterTypes})";

      // Check for conflicts
      if (generatedMethods.ContainsKey(fullSignature))
      {
        // Generate path-based name to avoid conflict
        string pathBasedName = GenerateMethodNameFromPath(operation);
        methodName = _options.UseAsyncSuffix ? $"{pathBasedName}Async" : pathBasedName;
        fullSignature = $"{methodName}({parameterTypes})";
      }

      generatedMethods[fullSignature] = operation;

      sb.AppendLine($"{indent}/// <summary>");
      sb.AppendLine($"{indent}/// {FormatXmlDocumentation(operation.Summary, indent)}");
      sb.AppendLine($"{indent}/// Operation: {operation.Method.ToUpper()} {operation.Path}");
      sb.AppendLine($"{indent}/// </summary>");
      // For void responses, use Task instead of Task<void>
      string taskReturnType = returnType == "void" ? "Task" : $"Task<{returnType}>";
      sb.AppendLine($"{indent}{taskReturnType} {methodName}({parameters});");
      sb.AppendLine();
    }

    sb.AppendLine("}");

    return new GeneratedFile
    {
      FileName = $"{interfaceName}.cs",
      Content = sb.ToString(),
    };
  }

  private List<GeneratedFile> GenerateRequestFiles(OpenApiAnalysis analysis)
  {
    List<GeneratedFile> requestFiles = new List<GeneratedFile>();

    switch (_options.RequestOrganization.Strategy.ToLowerInvariant())
    {
      case "individual_files":
        requestFiles.AddRange(GenerateIndividualRequestFiles(analysis));
        break;
      case "by_resource":
        requestFiles.AddRange(GenerateRequestFilesByResource(analysis));
        break;
      case "single_file":
      default:
        requestFiles.Add(GenerateSharedTypes(analysis));
        break;
    }

    return requestFiles;
  }

  private GeneratedFile GenerateSharedTypes(OpenApiAnalysis analysis)
  {
    StringBuilder sb = new();

    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine("using System.Linq;");
    sb.AppendLine("using System.Text.Json.Serialization;");
    sb.AppendLine("using System.Web;");
    sb.AppendLine();

    // Add nullable enable directive for auto-generated code when nullable is enabled
    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    if (analysis.ResponsePattern.IsWrapped)
    {
      sb.AppendLine("/// <summary>");
      sb.AppendLine("/// Standard API response wrapper");
      sb.AppendLine("/// </summary>");
      sb.AppendLine("public class ApiResponse<T>");
      sb.AppendLine("{");
      string dataPropertyName = analysis.ResponsePattern.DataProperty ?? "data";
      sb.AppendLine($"  [JsonPropertyName(\"{dataPropertyName}\")]");
      sb.AppendLine($"  public T {dataPropertyName.ToDotNetPascalCase()} {{ get; set; }} = default!;");

      if (!string.IsNullOrEmpty(analysis.ResponsePattern.MetaProperty))
      {
        sb.AppendLine($"  [JsonPropertyName(\"{analysis.ResponsePattern.MetaProperty}\")]");
        sb.AppendLine(
          $"  public {_options.ModelsNamespace}.Meta? {analysis.ResponsePattern.MetaProperty.ToDotNetPascalCase()} {{ get; set; }}");
      }

      sb.AppendLine("}");
      sb.AppendLine();
    }

    // Generate request classes for operations with query parameters
    GenerateRequestClasses(sb, analysis);

    return new GeneratedFile
    {
      FileName = "SharedTypes.cs",
      Content = sb.ToString(),
    };
  }

  private List<GeneratedFile> GenerateIndividualRequestFiles(OpenApiAnalysis analysis)
  {
    List<GeneratedFile> files = new List<GeneratedFile>();

    // Add base classes if requested
    if (_options.RequestOrganization.IncludeBaseClasses)
    {
      files.Add(GenerateBaseRequestFile());

      // Only generate ApiResponse if the API uses wrapped responses
      if (analysis.ResponsePattern.IsWrapped)
      {
        files.Add(GenerateApiResponseFile(analysis));
      }
    }

    // Generate individual request class files
    HashSet<string> generatedClasses = new HashSet<string>();

    foreach (ResourceOperation resource in analysis.Resources)
    {
      foreach (ApiOperation operation in resource.Operations)
      {
        List<ApiParameter> queryParams = operation.Parameters.Where(p => p.Location == "query").ToList();
        if (queryParams.Any())
        {
          string operationId = operation.OperationId;
          if (string.IsNullOrEmpty(operationId))
          {
            operationId = GenerateOperationId(operation);
          }
          string originalOperationId = operationId;
          operationId = SanitizeOperationId(operationId);

          // Apply overrides without sanitization
          string overriddenOperationId = ApplyOperationIdOverrides(originalOperationId, operation);
          if (overriddenOperationId != originalOperationId)
          {
            operationId = overriddenOperationId;
          }

          string className = $"{operationId.ToDotNetPascalCase()}Request";

          if (generatedClasses.Add(className))
          {
            files.Add(GenerateIndividualRequestFile(className, operation, queryParams));
          }
        }
      }
    }

    return files;
  }

  private List<GeneratedFile> GenerateRequestFilesByResource(OpenApiAnalysis analysis)
  {
    List<GeneratedFile> files = new List<GeneratedFile>();

    // Add base classes if requested
    if (_options.RequestOrganization.IncludeBaseClasses)
    {
      files.Add(GenerateBaseRequestFile());

      // Only generate ApiResponse if the API uses wrapped responses
      if (analysis.ResponsePattern.IsWrapped)
      {
        files.Add(GenerateApiResponseFile(analysis));
      }
    }

    // Group operations by resource
    Dictionary<string, List<(ApiOperation operation, List<ApiParameter> queryParams)>> resourceGroups =
      new Dictionary<string, List<(ApiOperation, List<ApiParameter>)>>();

    foreach (ResourceOperation resource in analysis.Resources)
    {
      string resourceKey = resource.Name.ToDotNetPascalCase();
      if (!resourceGroups.ContainsKey(resourceKey))
      {
        resourceGroups[resourceKey] = new List<(ApiOperation, List<ApiParameter>)>();
      }

      foreach (ApiOperation operation in resource.Operations)
      {
        List<ApiParameter> queryParams = operation.Parameters.Where(p => p.Location == "query").ToList();
        if (queryParams.Any())
        {
          resourceGroups[resourceKey].Add((operation, queryParams));
        }
      }
    }

    // Generate one file per resource
    foreach (KeyValuePair<string, List<(ApiOperation operation, List<ApiParameter> queryParams)>> group in resourceGroups)
    {
      if (group.Value.Any())
      {
        files.Add(GenerateResourceRequestFile(group.Key, group.Value));
      }
    }

    return files;
  }

  private GeneratedFile GenerateBaseClassesFile(OpenApiAnalysis analysis)
  {
    // For backwards compatibility, keep the combined file but it will be empty
    // Individual files are generated separately
    StringBuilder sb = new();

    sb.AppendLine($"// Base types are now in separate files: ApiResponse.cs and BaseRequest.cs");

    return new GeneratedFile
    {
      FileName = "BaseTypes.cs",
      Content = sb.ToString(),
    };
  }

  private GeneratedFile GenerateApiResponseFile(OpenApiAnalysis analysis)
  {
    StringBuilder sb = new();

    sb.AppendLine("using System.Text.Json.Serialization;");
    sb.AppendLine();

    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Standard API response wrapper");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("public class ApiResponse<T>");
    sb.AppendLine("{");
    string dataPropertyName = analysis.ResponsePattern.DataProperty ?? "data";
    sb.AppendLine($"  [JsonPropertyName(\"{dataPropertyName}\")]");
    sb.AppendLine($"  public T {dataPropertyName.ToDotNetPascalCase()} {{ get; set; }} = default!;");

    if (!string.IsNullOrEmpty(analysis.ResponsePattern.MetaProperty))
    {
      sb.AppendLine($"  [JsonPropertyName(\"{analysis.ResponsePattern.MetaProperty}\")]");
      sb.AppendLine($"  public {_options.ModelsNamespace}.Meta? {analysis.ResponsePattern.MetaProperty.ToDotNetPascalCase()} {{ get; set; }}");
    }

    sb.AppendLine("}");

    return new GeneratedFile
    {
      FileName = "ApiResponse.cs",
      Content = sb.ToString(),
    };
  }

  private GeneratedFile GenerateBaseRequestFile()
  {
    StringBuilder sb = new();

    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine("using System.Linq;");
    sb.AppendLine("using System.Web;");
    sb.AppendLine();

    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Base class for request objects");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("public abstract class BaseRequest");
    sb.AppendLine("{");
    sb.AppendLine("  public virtual string ToQueryString() => string.Empty;");
    sb.AppendLine("}");

    return new GeneratedFile
    {
      FileName = "BaseRequest.cs",
      Content = sb.ToString(),
    };
  }

  private GeneratedFile GenerateIndividualRequestFile(string className, ApiOperation operation, List<ApiParameter> queryParams)
  {
    StringBuilder sb = new();

    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine("using System.Linq;");
    sb.AppendLine("using System.Text.Json.Serialization;");
    sb.AppendLine("using System.Web;");
    sb.AppendLine();

    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    // Generate the request class
    GenerateSingleRequestClass(sb, className, operation, queryParams);

    string fileName = $"{className}.cs";
    if (_options.RequestOrganization.Strategy == "individual_files" &&
        !string.IsNullOrEmpty(_options.RequestOrganization.Directory))
    {
      fileName = $"{_options.RequestOrganization.Directory}/{className}.cs";
    }

    return new GeneratedFile
    {
      FileName = fileName,
      Content = sb.ToString(),
    };
  }

  private GeneratedFile GenerateResourceRequestFile(string resourceName, List<(ApiOperation operation, List<ApiParameter> queryParams)> operations)
  {
    StringBuilder sb = new();

    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine("using System.Linq;");
    sb.AppendLine("using System.Text.Json.Serialization;");
    sb.AppendLine("using System.Web;");
    sb.AppendLine();

    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    HashSet<string> generatedClasses = new HashSet<string>();

    foreach ((ApiOperation operation, List<ApiParameter> queryParams) in operations)
    {
      string operationId = operation.OperationId;
      if (string.IsNullOrEmpty(operationId))
      {
        operationId = GenerateOperationId(operation);
      }
      string originalOperationId = operationId;
      operationId = SanitizeOperationId(operationId);

      // Apply overrides without sanitization
      string overriddenOperationId = ApplyOperationIdOverrides(originalOperationId, operation);
      if (overriddenOperationId != originalOperationId)
      {
        operationId = overriddenOperationId;
      }

      string className = $"{operationId.ToDotNetPascalCase()}Request";

      if (generatedClasses.Add(className))
      {
        GenerateSingleRequestClass(sb, className, operation, queryParams);
        sb.AppendLine();
      }
    }

    string fileName = $"{resourceName}Requests.cs";
    if (_options.RequestOrganization.Strategy == "by_resource" &&
        !string.IsNullOrEmpty(_options.RequestOrganization.Directory))
    {
      fileName = $"{_options.RequestOrganization.Directory}/{resourceName}Requests.cs";
    }

    return new GeneratedFile
    {
      FileName = fileName,
      Content = sb.ToString(),
    };
  }

  private void GenerateSingleRequestClass(StringBuilder sb, string className, ApiOperation operation, List<ApiParameter> queryParams)
  {
    sb.AppendLine($"/// <summary>");
    sb.AppendLine($"/// Request parameters for {operation.Summary}");
    sb.AppendLine($"/// Operation: {operation.Method.ToUpper()} {operation.Path}");
    sb.AppendLine($"/// </summary>");
    sb.AppendLine($"public class {className} : BaseRequest");
    sb.AppendLine("{");

    // Generate properties
    foreach (ApiParameter param in queryParams)
    {
      string propertyName = param.Name.ToDotNetPascalCase();
      string propertyType = param.Type;

      if (!propertyType.EndsWith("?"))
      {
        propertyType += "?";
      }

      sb.AppendLine($"  /// <summary>");
      sb.AppendLine($"  /// {FormatXmlDocumentation(param.Description ?? param.Name)}");
      sb.AppendLine($"  /// </summary>");
      sb.AppendLine($"  [JsonPropertyName(\"{param.Name}\")]");
      sb.AppendLine($"  public {propertyType} {propertyName} {{ get; set; }}");
      sb.AppendLine();
    }

    // Generate ToQueryString method
    sb.AppendLine("  public override string ToQueryString()");
    sb.AppendLine("  {");
    sb.AppendLine("    Dictionary<string, object> queryParams = new Dictionary<string, object>();");
    sb.AppendLine();

    // Add fixed query parameters from operation overrides
    Dictionary<string, string> fixedParams = GetFixedQueryParameters(operation);
    foreach (var kvp in fixedParams)
    {
      sb.AppendLine($"    queryParams[\"{kvp.Key}\"] = \"{kvp.Value}\";");
    }
    if (fixedParams.Any())
    {
      sb.AppendLine();
    }
    // Debug: Add comment showing what operation this is for
    if (fixedParams.Any())
    {
      sb.AppendLine($"    // Fixed parameters added for {operation.Method} {operation.Path}");
    }

    foreach (ApiParameter param in queryParams)
    {
      string propertyName = param.Name.ToDotNetPascalCase();
      sb.AppendLine($"    if ({propertyName} != null)");
      sb.AppendLine($"      queryParams[\"{param.Name}\"] = {propertyName};");
    }

    sb.AppendLine();
    sb.AppendLine("    return queryParams.ToQueryString();");
    sb.AppendLine("  }");

    sb.AppendLine("}");
  }

  private void GenerateRequestClasses(StringBuilder sb, OpenApiAnalysis analysis)
  {
    sb.AppendLine();
    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Base class for request objects");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("public abstract class BaseRequest");
    sb.AppendLine("{");
    sb.AppendLine("  public virtual string ToQueryString() => string.Empty;");
    sb.AppendLine("}");
    sb.AppendLine();

    // Generate specific request classes for each operation that has query parameters
    HashSet<string> requestClasses = new();

    foreach (ResourceOperation resource in analysis.Resources)
    {
      foreach (ApiOperation operation in resource.Operations)
      {
        List<ApiParameter> queryParams = operation.Parameters.Where(p => p.Location == "query").ToList();
        if (queryParams.Any())
        {
          string operationId = operation.OperationId;
          if (string.IsNullOrEmpty(operationId))
          {
            operationId = GenerateOperationId(operation);
          }
          string originalOperationId = operationId;
          operationId = SanitizeOperationId(operationId);

          // Apply overrides without sanitization
          string overriddenOperationId = ApplyOperationIdOverrides(originalOperationId, operation);
          if (overriddenOperationId != originalOperationId)
          {
            operationId = overriddenOperationId;
          }

          string className = $"{operationId.ToDotNetPascalCase()}Request";

          if (requestClasses.Add(className)) // Only add if not already added
          {
            sb.AppendLine($"/// <summary>");
            sb.AppendLine($"/// Request parameters for {operation.Summary}");
            sb.AppendLine($"/// Operation: {operation.Method.ToUpper()} {operation.Path}");
            sb.AppendLine($"/// </summary>");
            sb.AppendLine($"public class {className} : BaseRequest");
            sb.AppendLine("{");

            // Generate actual properties for query parameters
            foreach (ApiParameter param in queryParams)
            {
              string propertyName = param.Name.ToDotNetPascalCase();
              string propertyType = param.Type;

              // Make query parameters optional by default
              if (!propertyType.EndsWith("?"))
              {
                propertyType += "?";
              }

              sb.AppendLine($"  /// <summary>");
              sb.AppendLine($"  /// {FormatXmlDocumentation(param.Description ?? param.Name)}");
              sb.AppendLine($"  /// </summary>");
              sb.AppendLine($"  [JsonPropertyName(\"{param.Name}\")]");
              sb.AppendLine($"  public {propertyType} {propertyName} {{ get; set; }}");
              sb.AppendLine();
            }

            // Generate ToQueryString implementation
            sb.AppendLine("  public override string ToQueryString()");
            sb.AppendLine("  {");
            sb.AppendLine("    Dictionary<string, object> queryParams = new Dictionary<string, object>();");
            sb.AppendLine();

            // Add fixed query parameters from operation overrides
            Dictionary<string, string> fixedParams = GetFixedQueryParameters(operation);
            foreach (var kvp in fixedParams)
            {
              sb.AppendLine($"    queryParams[\"{kvp.Key}\"] = \"{kvp.Value}\";");
            }
            if (fixedParams.Any())
            {
              sb.AppendLine();
            }
            // Debug: Add comment showing what operation this is for
            if (fixedParams.Any())
            {
              sb.AppendLine($"    // Fixed parameters added for {operation.Method} {operation.Path}");
            }

            foreach (ApiParameter param in queryParams)
            {
              string propertyName = param.Name.ToDotNetPascalCase();
              sb.AppendLine($"    if ({propertyName} != null)");
              sb.AppendLine($"      queryParams[\"{param.Name}\"] = {propertyName};");
            }

            sb.AppendLine();
            sb.AppendLine("    return queryParams.ToQueryString();");
            sb.AppendLine("  }");

            sb.AppendLine("}");
            sb.AppendLine();
          }
        }
      }
    }
  }

  private GeneratedFile GenerateProjectFile()
  {
    StringBuilder sb = new();

    sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
    sb.AppendLine();
    sb.AppendLine("  <PropertyGroup>");
    sb.AppendLine($"    <TargetFramework>{_targetFramework}</TargetFramework>");
    sb.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
    sb.AppendLine("    <Nullable>enable</Nullable>");
    sb.AppendLine("  </PropertyGroup>");
    sb.AppendLine();
    sb.AppendLine("  <ItemGroup>");
    // Use the models namespace to determine the project path and name
    string modelsProjectName = _options.ModelsNamespace.Split('.').Last() == "Models"
      ? _options.ModelsNamespace
      : _options.ModelsNamespace + ".Models";
    sb.AppendLine($"    <ProjectReference Include=\"../{modelsProjectName}/{modelsProjectName}.csproj\" PrivateAssets=\"all\" />");
    sb.AppendLine("  </ItemGroup>");
    sb.AppendLine();

    if (_options.UseILogger)
    {
      sb.AppendLine("  <ItemGroup>");
      sb.AppendLine("    <PackageReference Include=\"Microsoft.Extensions.Logging.Abstractions\" Version=\"8.0.0\" />");
      sb.AppendLine("  </ItemGroup>");
      sb.AppendLine();
    }

    sb.AppendLine("</Project>");

    return new GeneratedFile
    {
      FileName = $"{_options.ProjectName}.csproj",
      Content = sb.ToString(),
    };
  }

  private async Task WriteClientFilesAsync(GeneratedClientCode result, string outputPath)
  {
    string clientPath = Path.Combine(outputPath, _options.ProjectName);
    Directory.CreateDirectory(clientPath);

    // Write main client
    await File.WriteAllTextAsync(Path.Combine(clientPath, result.MainClient.FileName), result.MainClient.Content);

    // Write resource clients
    foreach (GeneratedFile client in result.ResourceClients)
    {
      await File.WriteAllTextAsync(Path.Combine(clientPath, client.FileName), client.Content);
    }

    // Write interfaces
    foreach (GeneratedFile interfaceFile in result.ResourceInterfaces)
    {
      await File.WriteAllTextAsync(Path.Combine(clientPath, interfaceFile.FileName), interfaceFile.Content);
    }

    // Write request files (organized according to strategy)
    foreach (GeneratedFile requestFile in result.RequestFiles)
    {
      string filePath = Path.Combine(clientPath, requestFile.FileName);

      // Create subdirectories if needed
      string? directory = Path.GetDirectoryName(filePath);
      if (!string.IsNullOrEmpty(directory))
      {
        Directory.CreateDirectory(directory);
      }

      await File.WriteAllTextAsync(filePath, requestFile.Content);
    }

    // Write project file
    await File.WriteAllTextAsync(Path.Combine(clientPath, result.ProjectFile.FileName), result.ProjectFile.Content);
  }

  // Helper methods
  private void GenerateAuthFactory(
    StringBuilder sb,
    string indent,
    OpenApiAnalysis analysis,
    AuthenticationScheme authScheme,
    List<ResourceOperation> uniqueResources)
  {
    string loggerParam = _options.UseILogger ? ", ILogger? logger = null" : "";
    string loggerArg = _options.UseILogger ? ", logger" : "";

    if (authScheme.Scheme == HttpAuthScheme.Basic)
    {
      sb.AppendLine($"{indent}/// <summary>");
      sb.AppendLine($"{indent}/// Create client with Basic Authentication");
      sb.AppendLine($"{indent}/// </summary>");
      sb.AppendLine($"{indent}public static {_options.ClientClassName} WithBasicAuth(string username, string password, string baseUrl = \"{analysis.BaseUrl}\"{loggerParam})");
      sb.AppendLine($"{indent}{{");
      sb.AppendLine($"{indent}{indent}HttpClient httpClient = CreateBasicAuthHttpClient(username, password, baseUrl);");
      sb.AppendLine($"{indent}{indent}return new {_options.ClientClassName}(httpClient, true{loggerArg});");
      sb.AppendLine($"{indent}}}");
    }
    else if (authScheme.In == AuthSchemeLocation.Cookie)
    {
      sb.AppendLine($"{indent}/// <summary>");
      sb.AppendLine($"{indent}/// Create client with cookie-based authentication");
      sb.AppendLine($"{indent}/// </summary>");
      sb.AppendLine($"{indent}public static {_options.ClientClassName} WithCookie(string sessionToken, string baseUrl = \"{analysis.BaseUrl}\"{loggerParam})");
      sb.AppendLine($"{indent}{{");
      sb.AppendLine($"{indent}{indent}HttpClient httpClient = CreateCookieAuthHttpClient(sessionToken, \"{authScheme.CookieName}\", baseUrl);");
      sb.AppendLine($"{indent}{indent}return new {_options.ClientClassName}(httpClient, true{loggerArg});");
      sb.AppendLine($"{indent}}}");
    }
    else if (authScheme.Scheme == HttpAuthScheme.Bearer)
    {
      sb.AppendLine($"{indent}/// <summary>");
      sb.AppendLine($"{indent}/// Create client with Bearer token authentication");
      sb.AppendLine($"{indent}/// </summary>");
      sb.AppendLine($"{indent}public static {_options.ClientClassName} WithBearer(string bearerToken, string baseUrl = \"{analysis.BaseUrl}\"{loggerParam})");
      sb.AppendLine($"{indent}{{");
      sb.AppendLine($"{indent}{indent}HttpClient httpClient = CreateTokenAuthHttpClient(bearerToken, baseUrl, \"Authorization\", true);");
      sb.AppendLine($"{indent}{indent}return new {_options.ClientClassName}(httpClient, true{loggerArg});");
      sb.AppendLine($"{indent}}}");
    }
    else
    {
      sb.AppendLine($"{indent}/// <summary>");
      sb.AppendLine($"{indent}/// Create client with API key authentication");
      sb.AppendLine($"{indent}/// </summary>");
      sb.AppendLine($"{indent}public static {_options.ClientClassName} WithApiKey(string apiKey, string baseUrl = \"{analysis.BaseUrl}\"{loggerParam})");
      sb.AppendLine($"{indent}{{");
      sb.AppendLine($"{indent}{indent}HttpClient httpClient = CreateTokenAuthHttpClient(apiKey, baseUrl, \"{authScheme.HeaderName}\", false);");
      sb.AppendLine($"{indent}{indent}return new {_options.ClientClassName}(httpClient, true{loggerArg});");
      sb.AppendLine($"{indent}}}");
    }

    sb.AppendLine();
  }

  private string GetResourceClientName(string resourceName)
  {
    string sanitizedName = SanitizeOperationId(resourceName);
    string pascalName = sanitizedName.ToDotNetPascalCase();

    // Apply type name overrides to resource names (e.g., "Oauth" -> "OAuth")
    pascalName = ApplyTypeNameOverrides(pascalName);

    return $"{pascalName}Client";
  }

  private string GetResourceInterfaceName(string resourceName)
  {
    string sanitizedName = SanitizeOperationId(resourceName);
    string pascalName = sanitizedName.ToDotNetPascalCase();

    // Apply type name overrides to resource names (e.g., "Oauth" -> "OAuth")
    pascalName = ApplyTypeNameOverrides(pascalName);

    return $"I{pascalName}Client";
  }



  private string ExtractParameterTypes(string parameters)
  {
    if (string.IsNullOrEmpty(parameters))
    {
      return string.Empty;
    }

    // Split by comma and extract just the type part (before the parameter name)
    string[] paramParts = parameters.Split(',', StringSplitOptions.RemoveEmptyEntries);
    List<string> types = new();

    foreach (string part in paramParts)
    {
      string trimmed = part.Trim();
      int spaceIndex = trimmed.LastIndexOf(' ');
      if (spaceIndex > 0)
      {
        types.Add(trimmed.Substring(0, spaceIndex).Trim());
      }
      else
      {
        types.Add(trimmed);
      }
    }

    return string.Join(", ", types);
  }

  /// <summary>
  /// Detects the common path prefix from all paths in the OpenAPI document
  /// </summary>
  private string DetectCommonPathPrefix(OpenApiDocument document)
  {
    if (document.Paths == null || !document.Paths.Any())
    {
      return "";
    }

    List<string> allPaths = document.Paths.Keys.ToList();
    if (allPaths.Count == 1)
    {
      return "";
    }

    // Find the common prefix
    string commonPrefix = allPaths[0];
    foreach (string path in allPaths.Skip(1))
    {
      commonPrefix = GetCommonPrefix(commonPrefix, path);
      if (string.IsNullOrEmpty(commonPrefix))
      {
        break;
      }
    }

    // Ensure the common prefix ends at a path segment boundary with trailing slash
    if (!string.IsNullOrEmpty(commonPrefix) && !commonPrefix.EndsWith("/"))
    {
      int lastSlashIndex = commonPrefix.LastIndexOf('/');
      if (lastSlashIndex >= 0)
      {
        commonPrefix = commonPrefix.Substring(0, lastSlashIndex + 1);
      }
    }

    // Only consider it a common prefix if it's meaningful (more than just "/")
    // Keep the trailing slash for proper stripping
    return commonPrefix.Length > 1 ? commonPrefix : "";
  }

  /// <summary>
  /// Gets the common prefix between two strings
  /// </summary>
  private string GetCommonPrefix(string str1, string str2)
  {
    int minLength = Math.Min(str1.Length, str2.Length);
    int commonLength = 0;

    for (int i = 0; i < minLength; i++)
    {
      if (str1[i] == str2[i])
      {
        commonLength++;
      }
      else
      {
        break;
      }
    }

    return str1.Substring(0, commonLength);
  }

  private bool IsPrimitiveType(string type)
  {
    return type switch
    {
      "int" or "decimal" or "bool" or "double" or "float" or "long" => true,
      _ => false,
    };
  }


  private string FormatXmlDocumentation(string description, string indent = "  ")
  {
    if (string.IsNullOrEmpty(description))
    {
      return description;
    }

    // Proper XML encode the content
    string encoded = XmlEncode(description);

    // Split into lines and prefix each with the comment prefix
    string[] lines = encoded.Split('\n', StringSplitOptions.None);
    StringBuilder result = new();

    for (int i = 0; i < lines.Length; i++)
    {
      string line = lines[i].TrimEnd('\r'); // Remove carriage returns
      if (i == 0)
      {
        result.Append(line);
      }
      else
      {
        result.AppendLine();
        result.Append($"{indent}/// {line}");
      }
    }

    return result.ToString();
  }

  private string XmlEncode(string text)
  {
    if (string.IsNullOrEmpty(text))
    {
      return text;
    }

    return text
      .Replace("&", "&amp;") // Must be first
      .Replace("<", "&lt;")
      .Replace(">", "&gt;")
      .Replace("\"", "&quot;")
      .Replace("'", "&apos;");
  }

  /// <summary>
  /// Fully qualifies model types that might conflict with namespace names.
  /// Detects any type that matches a namespace segment and fully qualifies it.
  /// </summary>
  private string ApplyTypeNameOverrides(string typeName)
  {
    // Check if the type (without array suffix) matches any configured type name override
    bool isArray = typeName.EndsWith("[]");
    string baseType = isArray ? typeName.Substring(0, typeName.Length - 2) : typeName;

    // Apply type name overrides from configuration
    foreach (TypeNameOverride typeOverride in _typeNameOverrides)
    {
      if (typeOverride.OriginalName == baseType)
      {
        baseType = typeOverride.NewName;
        break;
      }
    }

    // Restore array suffix if it was present
    return isArray ? $"{baseType}[]" : baseType;
  }

  /// <summary>
  /// Resolves a schema name to its canonical form if it was deduplicated.
  /// Returns the original name if no deduplication mapping exists.
  /// </summary>
  private string ResolveCanonicalSchemaName(string schemaName)
  {
    if (_modelDecisions != null &&
        _modelDecisions.TryGetValue(schemaName, out ModelGenerationDecision? decision) &&
        decision.SkipGeneration &&
        decision.CanonicalSchemaName != null)
    {
      return decision.CanonicalSchemaName;
    }
    return schemaName;
  }

  private string FullyQualifyModelTypes(string typeExpression)
  {
    if (string.IsNullOrEmpty(typeExpression))
    {
      return typeExpression;
    }

    // Get the client namespace segments (e.g., "GeneratedApi.Client" -> ["GeneratedApi", "Client"])
    string[] clientNamespaceSegments = _options.Namespace.Split('.');

    // For each segment that could conflict (typically the last segment like "Client")
    foreach (string segment in clientNamespaceSegments)
    {
      // Skip if it's already fully qualified
      if (typeExpression.Contains($"{_options.ModelsNamespace}.{segment}"))
      {
        continue;
      }

      // Replace type name with fully qualified version if it appears as a standalone type
      // Use word boundaries to avoid partial matches
      string pattern = $@"\b{segment}\b";
      if (System.Text.RegularExpressions.Regex.IsMatch(typeExpression, pattern))
      {
        typeExpression = System.Text.RegularExpressions.Regex.Replace(
          typeExpression,
          pattern,
          $"{_options.ModelsNamespace}.{segment}");
      }
    }

    return typeExpression;
  }

  private Dictionary<string, string> GetFixedQueryParameters(ApiOperation operation)
  {
    Dictionary<string, string> fixedParams = new();

    foreach (OperationOverride opOverride in _operationOverrides)
    {
      // Check if this override applies to the operation
      if (!DoesOperationOverrideApply(opOverride, operation))
        continue;

      // Add fixed query parameters from this override
      foreach (var kvp in opOverride.FixedQueryParameters)
      {
        fixedParams[kvp.Key] = kvp.Value;
      }
    }

    return fixedParams;
  }

  private bool DoesOperationOverrideApply(OperationOverride opOverride, ApiOperation operation)
  {
    // Check HTTP method using regex
    if (!string.IsNullOrEmpty(opOverride.HttpMethodFilter) &&
        !Regex.IsMatch(operation.Method, opOverride.HttpMethodFilter, RegexOptions.IgnoreCase))
    {
      return false;
    }

    // Check path using regex
    if (!string.IsNullOrEmpty(opOverride.PathFilter) &&
        !Regex.IsMatch(operation.Path, opOverride.PathFilter))
    {
      return false;
    }

    // Check excluded paths
    foreach (string excludePath in opOverride.ExcludePaths)
    {
      if (Regex.IsMatch(operation.Path, excludePath))
      {
        return false;
      }
    }

    // Check content type - skip for now as ApiOperation doesn't have RequestContentTypes
    // TODO: Add content type checking if needed
    // if (!string.IsNullOrEmpty(opOverride.ContentType))
    // {
    //   // Would need to check request content type from OpenAPI spec
    // }

    // Check for required body parameter
    if (!string.IsNullOrEmpty(opOverride.HasBodyParameter))
    {
      bool hasParameter = operation.Parameters.Any(p => p.Name == opOverride.HasBodyParameter);
      if (!hasParameter)
      {
        return false;
      }
    }

    return true;
  }

  private GeneratedFile GenerateQueryStringExtensions()
  {
    StringBuilder sb = new();

    // Add using statements
    sb.AppendLine("using System;");
    sb.AppendLine("using System.Collections.Generic;");
    sb.AppendLine("using System.Globalization;");
    sb.AppendLine("using System.Linq;");
    sb.AppendLine("using System.Reflection;");
    sb.AppendLine("using System.Text;");
    sb.AppendLine("using System.Web;");
    sb.AppendLine();
    sb.AppendLine("#nullable enable");
    sb.AppendLine();
    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    // Generate extension class
    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Extension methods for building query strings and URL templates");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("public static class QueryStringExtensions");
    sb.AppendLine("{");
    sb.AppendLine("  /// <summary>");
    sb.AppendLine("  /// Converts a dictionary of query parameters to a URL-encoded query string");
    sb.AppendLine("  /// </summary>");
    sb.AppendLine("  /// <param name=\"queryParams\">Dictionary of query parameters</param>");
    sb.AppendLine("  /// <returns>URL-encoded query string starting with '?' or empty string if no parameters</returns>");
    sb.AppendLine("  public static string ToQueryString(this Dictionary<string, object> queryParams)");
    sb.AppendLine("  {");
    sb.AppendLine("    if (queryParams.Count == 0) return string.Empty;");
    sb.AppendLine();
    sb.AppendLine("    IEnumerable<string> encodedParams = queryParams.Select(kvp =>");
    sb.AppendLine("    {");
    sb.AppendLine("      string valueStr = kvp.Value switch");
    sb.AppendLine("      {");
    sb.AppendLine("        null => string.Empty,");
    sb.AppendLine("        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),");
    sb.AppendLine("        _ => kvp.Value.ToString() ?? string.Empty");
    sb.AppendLine("      };");
    sb.AppendLine("      return $\"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(valueStr)}\";");
    sb.AppendLine("    });");
    sb.AppendLine();
    sb.AppendLine("    return \"?\" + string.Join(\"&\", encodedParams);");
    sb.AppendLine("  }");
    sb.AppendLine();
    sb.AppendLine("  /// <summary>");
    sb.AppendLine("  /// Builds a URL from a template string by safely substituting parameters and appending query string");
    sb.AppendLine("  /// </summary>");
    sb.AppendLine("  /// <param name=\"template\">URL template with {param} placeholders (case-sensitive)</param>");
    sb.AppendLine("  /// <param name=\"pathParams\">Dictionary with path parameter values. Keys must match template placeholders exactly.</param>");
    sb.AppendLine("  /// <param name=\"request\">Request object with ToQueryString method for query parameters</param>");
    sb.AppendLine("  /// <returns>Complete URL with path parameters substituted and query string appended</returns>");
    sb.AppendLine("  public static string BuildUrl(this string template, Dictionary<string, object>? pathParams = null, object? request = null)");
    sb.AppendLine("  {");
    sb.AppendLine("    string url = template;");
    sb.AppendLine();
    sb.AppendLine("    // Handle path parameter substitution");
    sb.AppendLine("    if (pathParams != null)");
    sb.AppendLine("    {");
    sb.AppendLine("      // Dictionary keys must match template placeholders exactly (case-sensitive)");
    sb.AppendLine();
    sb.AppendLine("      StringBuilder result = new StringBuilder();");
    sb.AppendLine("      int lastIndex = 0;");
    sb.AppendLine();
    sb.AppendLine("      while (lastIndex < template.Length)");
    sb.AppendLine("      {");
    sb.AppendLine("        int openBrace = template.IndexOf('{', lastIndex);");
    sb.AppendLine("        if (openBrace == -1)");
    sb.AppendLine("        {");
    sb.AppendLine("          // No more placeholders, append the rest");
    sb.AppendLine("          result.Append(template.Substring(lastIndex));");
    sb.AppendLine("          break;");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        // Append literal part before the placeholder");
    sb.AppendLine("        result.Append(template.Substring(lastIndex, openBrace - lastIndex));");
    sb.AppendLine();
    sb.AppendLine("        int closeBrace = template.IndexOf('}', openBrace);");
    sb.AppendLine("        if (closeBrace == -1)");
    sb.AppendLine("        {");
    sb.AppendLine("          // Malformed template, append the rest as-is");
    sb.AppendLine("          result.Append(template.Substring(openBrace));");
    sb.AppendLine("          break;");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        // Extract parameter name and substitute");
    sb.AppendLine("        string paramName = template.Substring(openBrace + 1, closeBrace - openBrace - 1);");
    sb.AppendLine("        if (pathParams.TryGetValue(paramName, out object? paramValue))");
    sb.AppendLine("        {");
    sb.AppendLine("          string valueStr = paramValue switch");
    sb.AppendLine("          {");
    sb.AppendLine("            null => string.Empty,");
    sb.AppendLine("            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),");
    sb.AppendLine("            _ => paramValue.ToString() ?? string.Empty");
    sb.AppendLine("          };");
    sb.AppendLine("          result.Append(Uri.EscapeDataString(valueStr));");
    sb.AppendLine("        }");
    sb.AppendLine("        else");
    sb.AppendLine("        {");
    sb.AppendLine("          // Parameter not found, keep placeholder");
    sb.AppendLine("          result.Append(template.Substring(openBrace, closeBrace - openBrace + 1));");
    sb.AppendLine("        }");
    sb.AppendLine();
    sb.AppendLine("        lastIndex = closeBrace + 1;");
    sb.AppendLine("      }");
    sb.AppendLine();
    sb.AppendLine("      url = result.ToString();");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine("    // Handle query string appending");
    sb.AppendLine("    if (request != null)");
    sb.AppendLine("    {");
    sb.AppendLine("      MethodInfo? toQueryStringMethod = request.GetType().GetMethod(\"ToQueryString\");");
    sb.AppendLine("      if (toQueryStringMethod != null)");
    sb.AppendLine("      {");
    sb.AppendLine("        string? queryString = toQueryStringMethod.Invoke(request, null) as string;");
    sb.AppendLine("        if (!string.IsNullOrEmpty(queryString))");
    sb.AppendLine("        {");
    sb.AppendLine("          url += queryString;");
    sb.AppendLine("        }");
    sb.AppendLine("      }");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine("    return url;");
    sb.AppendLine("  }");
    sb.AppendLine("}");

    return new GeneratedFile
    {
      FileName = "QueryStringExtensions.cs",
      Content = sb.ToString(),
    };
  }

  private GeneratedFile GenerateMultipartContentExtensions()
  {
    StringBuilder sb = new();
    sb.AppendLine("using System.Net.Http;");
    sb.AppendLine("using System.Reflection;");
    sb.AppendLine("using System.Text.Json.Serialization;");
    sb.AppendLine();
    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();
    sb.AppendLine("internal static class MultipartContentExtensions");
    sb.AppendLine("{");
    sb.AppendLine("  /// <summary>");
    sb.AppendLine("  /// Converts a DTO object to MultipartFormDataContent for file upload endpoints.");
    sb.AppendLine("  /// Properties of type byte[] are added as file content, all others as string fields.");
    sb.AppendLine("  /// Uses JsonPropertyName attribute for field names.");
    sb.AppendLine("  /// </summary>");
    sb.AppendLine("  public static MultipartFormDataContent ToMultipartContent(this object dto)");
    sb.AppendLine("  {");
    sb.AppendLine("    MultipartFormDataContent content = new();");
    sb.AppendLine();
    sb.AppendLine("    foreach (PropertyInfo prop in dto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))");
    sb.AppendLine("    {");
    sb.AppendLine("      object? value = prop.GetValue(dto);");
    sb.AppendLine("      if (value == null) continue;");
    sb.AppendLine();
    sb.AppendLine("      // Use JsonPropertyName attribute for the field name, fall back to property name");
    sb.AppendLine("      string fieldName = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? prop.Name;");
    sb.AppendLine();
    sb.AppendLine("      if (value is byte[] bytes)");
    sb.AppendLine("      {");
    sb.AppendLine("        ByteArrayContent fileContent = new(bytes);");
    sb.AppendLine("        content.Add(fileContent, fieldName, fieldName);");
    sb.AppendLine("      }");
    sb.AppendLine("      else if (value is Stream stream)");
    sb.AppendLine("      {");
    sb.AppendLine("        StreamContent streamContent = new(stream);");
    sb.AppendLine("        content.Add(streamContent, fieldName, fieldName);");
    sb.AppendLine("      }");
    sb.AppendLine("      else if (value is bool boolValue)");
    sb.AppendLine("      {");
    sb.AppendLine("        content.Add(new StringContent(boolValue.ToString().ToLowerInvariant()), fieldName);");
    sb.AppendLine("      }");
    sb.AppendLine("      else if (value is DateTime dateTime)");
    sb.AppendLine("      {");
    sb.AppendLine("        content.Add(new StringContent(dateTime.ToString(\"O\")), fieldName);");
    sb.AppendLine("      }");
    sb.AppendLine("      else if (value is DateTimeOffset dateTimeOffset)");
    sb.AppendLine("      {");
    sb.AppendLine("        content.Add(new StringContent(dateTimeOffset.ToString(\"O\")), fieldName);");
    sb.AppendLine("      }");
    sb.AppendLine("      else");
    sb.AppendLine("      {");
    sb.AppendLine("        content.Add(new StringContent(value.ToString() ?? \"\"), fieldName);");
    sb.AppendLine("      }");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine("    return content;");
    sb.AppendLine("  }");
    sb.AppendLine("}");

    return new GeneratedFile
    {
      FileName = "MultipartContentExtensions.cs",
      Content = sb.ToString(),
    };
  }

  private GeneratedFile GenerateFormUrlEncodedContentExtensions()
  {
    StringBuilder sb = new();
    sb.AppendLine("using System.Net.Http;");
    sb.AppendLine("using System.Reflection;");
    sb.AppendLine("using System.Text.Json.Serialization;");
    sb.AppendLine();
    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();
    sb.AppendLine("internal static class FormUrlEncodedContentExtensions");
    sb.AppendLine("{");
    sb.AppendLine("  /// <summary>");
    sb.AppendLine("  /// Converts a DTO object to FormUrlEncodedContent for OAuth2 token endpoints and similar.");
    sb.AppendLine("  /// Uses JsonPropertyName attribute for field names. Null properties are omitted.");
    sb.AppendLine("  /// </summary>");
    sb.AppendLine("  public static FormUrlEncodedContent ToFormUrlEncodedContent(this object dto)");
    sb.AppendLine("  {");
    sb.AppendLine("    List<KeyValuePair<string, string>> fields = new();");
    sb.AppendLine();
    sb.AppendLine("    foreach (PropertyInfo prop in dto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))");
    sb.AppendLine("    {");
    sb.AppendLine("      object? value = prop.GetValue(dto);");
    sb.AppendLine("      if (value == null) continue;");
    sb.AppendLine();
    sb.AppendLine("      string fieldName = prop.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? prop.Name;");
    sb.AppendLine();
    sb.AppendLine("      if (value is bool boolValue)");
    sb.AppendLine("        fields.Add(new(fieldName, boolValue.ToString().ToLowerInvariant()));");
    sb.AppendLine("      else if (value is int or long or short or byte)");
    sb.AppendLine("        fields.Add(new(fieldName, value.ToString()!));");
    sb.AppendLine("      else");
    sb.AppendLine("        fields.Add(new(fieldName, value.ToString() ?? \"\"));");
    sb.AppendLine("    }");
    sb.AppendLine();
    sb.AppendLine("    return new FormUrlEncodedContent(fields);");
    sb.AppendLine("  }");
    sb.AppendLine("}");

    return new GeneratedFile
    {
      FileName = "FormUrlEncodedContentExtensions.cs",
      Content = sb.ToString(),
    };
  }

  private GeneratedFile GenerateJsonConfig()
  {
    StringBuilder sb = new();
    string indent = _formatting.UseSpaces ? new string(' ', _formatting.IndentWidth) : "\t";

    // File header
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("using System.Text.Json;");
    sb.AppendLine("using System.Text.Json.Serialization;");
    sb.AppendLine($"using {_options.ModelsNamespace};");
    sb.AppendLine();

    // Add nullable enable directive
    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Internal JSON serialization configuration.");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("internal static class JsonConfig");
    sb.AppendLine("{");
    sb.AppendLine($"{indent}/// <summary>");
    sb.AppendLine($"{indent}/// Default JSON serializer options for all API operations.");
    sb.AppendLine($"{indent}/// </summary>");
    sb.AppendLine($"{indent}internal static readonly JsonSerializerOptions Default = CreateOptions();");
    sb.AppendLine();
    sb.AppendLine($"{indent}private static JsonSerializerOptions CreateOptions()");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}JsonSerializerOptions options = new()");
    sb.AppendLine($"{indent}{indent}{{");
    sb.AppendLine($"{indent}{indent}{indent}DefaultIgnoreCondition = JsonIgnoreCondition.{GetIgnoreCondition()}");
    sb.AppendLine($"{indent}{indent}}};");
    sb.AppendLine($"{indent}{indent}options.Converters.Add(new SmartEnumConverterFactory());");
    sb.AppendLine($"{indent}{indent}return options;");
    sb.AppendLine($"{indent}}}");
    sb.AppendLine("}");

    return new GeneratedFile
    {
      FileName = "_JsonConfig.g.cs",
      Content = sb.ToString(),
    };
  }

  private GeneratedFile GenerateSmartEnumConverter()
  {
    StringBuilder sb = new();

    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("using System.ComponentModel;");
    sb.AppendLine("using System.Reflection;");
    sb.AppendLine("using System.Runtime.Serialization;");
    sb.AppendLine("using System.Text.Json;");
    sb.AppendLine("using System.Text.Json.Serialization;");
    sb.AppendLine();

    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    // Read the SmartEnumConverter source from the embedded template
    sb.AppendLine(@"/// <summary>
/// Smart JSON converter for enhanced enums that handles both raw values and enum names.
/// </summary>
public class SmartEnumConverter<TEnum> : JsonConverter<TEnum?> where TEnum : struct, Enum
{
  private readonly Dictionary<string, TEnum> _stringToEnum;
  private readonly Dictionary<TEnum, string> _enumToString;
  private readonly TEnum? _unknownValue;

  public SmartEnumConverter()
  {
    _stringToEnum = BuildStringToEnumMapping();
    _enumToString = BuildEnumToStringMapping();
    _unknownValue = GetUnknownValue();
  }

  public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    return reader.TokenType switch
    {
      JsonTokenType.String => ParseStringValue(reader.GetString()),
      JsonTokenType.Number => ParseNumberValue(reader),
      JsonTokenType.Null => null,
      _ => _unknownValue,
    };
  }

  public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
  {
    if (value == null)
    {
      writer.WriteNullValue();
      return;
    }

    if (_enumToString.TryGetValue(value.Value, out string? rawValue))
    {
      writer.WriteStringValue(rawValue);
    }
    else
    {
      writer.WriteStringValue(value.ToString());
    }
  }

  private TEnum? ParseStringValue(string? value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return null;
    }

    if (_stringToEnum.TryGetValue(value, out TEnum exact))
    {
      return exact;
    }

    if (int.TryParse(value, out int numValue))
    {
      return ParseNumericValue(numValue);
    }

    if (Enum.TryParse<TEnum>(value, true, out TEnum parsed))
    {
      return parsed;
    }

    return _unknownValue;
  }

  private TEnum? ParseNumberValue(Utf8JsonReader reader)
  {
    if (reader.TryGetInt32(out int intValue))
    {
      return ParseNumericValue(intValue);
    }

    return _unknownValue;
  }

  private TEnum? ParseNumericValue(int value)
  {
    if (Enum.IsDefined(typeof(TEnum), value))
    {
      return (TEnum)(object)value;
    }

    string underscoreName = $""_{value}"";
    if (_stringToEnum.TryGetValue(underscoreName, out TEnum prefixed))
    {
      return prefixed;
    }

    return _unknownValue;
  }

  private Dictionary<string, TEnum> BuildStringToEnumMapping()
  {
    Dictionary<string, TEnum> mapping = new(StringComparer.OrdinalIgnoreCase);

    foreach (TEnum enumValue in Enum.GetValues<TEnum>())
    {
      string enumName = enumValue.ToString();
      mapping[enumName] = enumValue;

      FieldInfo? memberInfo = typeof(TEnum).GetField(enumName);
      EnumMemberAttribute? enumMemberAttr = memberInfo?.GetCustomAttribute<EnumMemberAttribute>();
      if (enumMemberAttr?.Value != null)
      {
        mapping[enumMemberAttr.Value] = enumValue;
      }

      if (enumName.StartsWith(""_"") && int.TryParse(enumName.Substring(1), out int numericValue))
      {
        mapping[numericValue.ToString()] = enumValue;
      }
    }

    return mapping;
  }

  private Dictionary<TEnum, string> BuildEnumToStringMapping()
  {
    Dictionary<TEnum, string> mapping = new();

    foreach (TEnum enumValue in Enum.GetValues<TEnum>())
    {
      string enumName = enumValue.ToString();

      FieldInfo? memberInfo = typeof(TEnum).GetField(enumName);
      EnumMemberAttribute? enumMemberAttr = memberInfo?.GetCustomAttribute<EnumMemberAttribute>();

      if (enumMemberAttr?.Value != null)
      {
        mapping[enumValue] = enumMemberAttr.Value;
      }
      else if (enumName.StartsWith(""_"") && int.TryParse(enumName.Substring(1), out int numericValue))
      {
        mapping[enumValue] = numericValue.ToString();
      }
      else
      {
        mapping[enumValue] = enumName;
      }
    }

    return mapping;
  }

  private TEnum? GetUnknownValue()
  {
    if (Enum.TryParse<TEnum>(""Unknown"", true, out TEnum unknown))
    {
      return unknown;
    }

    if (Enum.IsDefined(typeof(TEnum), -1))
    {
      return (TEnum)(object)-1;
    }

    TEnum[] values = Enum.GetValues<TEnum>();
    return values.Length > 0 ? values[0] : null;
  }
}

/// <summary>
/// Non-nullable version of the smart enum converter.
/// </summary>
public class SmartEnumConverterNonNullable<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
  private readonly SmartEnumConverter<TEnum> _nullableConverter = new();

  public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    return _nullableConverter.Read(ref reader, typeToConvert, options) ?? default(TEnum);
  }

  public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
  {
    _nullableConverter.Write(writer, value, options);
  }
}

/// <summary>
/// Factory for creating smart enum converters.
/// </summary>
public class SmartEnumConverterFactory : JsonConverterFactory
{
  public override bool CanConvert(Type typeToConvert)
  {
    return typeToConvert.IsEnum ||
           (typeToConvert.IsGenericType &&
            typeToConvert.GetGenericTypeDefinition() == typeof(Nullable<>) &&
            typeToConvert.GetGenericArguments()[0].IsEnum);
  }

  public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
  {
    if (typeToConvert.IsEnum)
    {
      return (JsonConverter)Activator.CreateInstance(
        typeof(SmartEnumConverterNonNullable<>).MakeGenericType(typeToConvert))!;
    }
    else
    {
      Type enumType = typeToConvert.GetGenericArguments()[0];
      return (JsonConverter)Activator.CreateInstance(
        typeof(SmartEnumConverter<>).MakeGenericType(enumType))!;
    }
  }
}");

    return new GeneratedFile
    {
      FileName = "_SmartEnumConverter.g.cs",
      Content = sb.ToString(),
    };
  }

  private GeneratedFile GenerateHttpClientLog()
  {
    StringBuilder sb = new();
    string indent = _formatting.UseSpaces ? new string(' ', _formatting.IndentWidth) : "\t";

    // File header
    sb.AppendLine("// <auto-generated />");
    sb.AppendLine("using Microsoft.Extensions.Logging;");
    sb.AppendLine("using System;");
    sb.AppendLine();

    // Add nullable enable directive
    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// High-performance HTTP client logging using LoggerMessage source generator.");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("internal static partial class HttpClientLog");
    sb.AppendLine("{");

    // 1001: Request started (Debug)
    sb.AppendLine($"{indent}[LoggerMessage(EventId = 1001, Level = LogLevel.Debug,");
    sb.AppendLine($"{indent}{indent}Message = \"Making {{Method}} request to {{Url}}\")]");
    sb.AppendLine($"{indent}private static partial void LogDebugRequestStartedCore(ILogger logger, string method, string url);");
    sb.AppendLine();
    sb.AppendLine($"{indent}public static void LogDebugRequestStarted(ILogger? logger, string method, string url)");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}if (logger != null)");
    sb.AppendLine($"{indent}{indent}{indent}LogDebugRequestStartedCore(logger, method, url);");
    sb.AppendLine($"{indent}}}");
    sb.AppendLine();

    // 1002: Request completed (Debug)
    sb.AppendLine($"{indent}[LoggerMessage(EventId = 1002, Level = LogLevel.Debug,");
    sb.AppendLine($"{indent}{indent}Message = \"Received {{StatusCode}} response from {{Method}} {{Url}} in {{DurationMs}}ms\")]");
    sb.AppendLine($"{indent}private static partial void LogDebugRequestCompletedCore(ILogger logger, int statusCode, string method, string url, long durationMs);");
    sb.AppendLine();
    sb.AppendLine($"{indent}public static void LogDebugRequestCompleted(ILogger? logger, int statusCode, string method, string url, long durationMs)");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}if (logger != null)");
    sb.AppendLine($"{indent}{indent}{indent}LogDebugRequestCompletedCore(logger, statusCode, method, url, durationMs);");
    sb.AppendLine($"{indent}}}");
    sb.AppendLine();

    // 3001: Request failed (Error)
    sb.AppendLine($"{indent}[LoggerMessage(EventId = 3001, Level = LogLevel.Error,");
    sb.AppendLine($"{indent}{indent}Message = \"HTTP {{StatusCode}} error for {{Method}} {{Url}}. Response: {{ResponseContent}}\")]");
    sb.AppendLine($"{indent}private static partial void LogErrorRequestFailedCore(ILogger logger, int statusCode, string method, string url, string responseContent, Exception? exception);");
    sb.AppendLine();
    sb.AppendLine($"{indent}public static void LogErrorRequestFailed(ILogger? logger, int statusCode, string method, string url, string responseContent, Exception? exception)");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}if (logger != null)");
    sb.AppendLine($"{indent}{indent}{indent}LogErrorRequestFailedCore(logger, statusCode, method, url, responseContent, exception);");
    sb.AppendLine($"{indent}}}");
    sb.AppendLine();

    // 1003: Request body (Trace)
    sb.AppendLine($"{indent}[LoggerMessage(EventId = 1003, Level = LogLevel.Trace,");
    sb.AppendLine($"{indent}{indent}Message = \"{{Method}} request body [{{ContentType}}]: {{Body}}\")]");
    sb.AppendLine($"{indent}private static partial void LogTraceRequestBodyCore(ILogger logger, string method, string contentType, string body);");
    sb.AppendLine();
    sb.AppendLine($"{indent}public static void LogTraceRequestBody(ILogger? logger, string method, string contentType, string body)");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}if (logger != null)");
    sb.AppendLine($"{indent}{indent}{indent}LogTraceRequestBodyCore(logger, method, contentType, body);");
    sb.AppendLine($"{indent}}}");
    sb.AppendLine();

    // 2003: Response body (Trace)
    sb.AppendLine($"{indent}[LoggerMessage(EventId = 2003, Level = LogLevel.Trace,");
    sb.AppendLine($"{indent}{indent}Message = \"Response from {{Url}}: {{Body}}\")]");
    sb.AppendLine($"{indent}private static partial void LogTraceResponseBodyCore(ILogger logger, string url, string body);");
    sb.AppendLine();
    sb.AppendLine($"{indent}public static void LogTraceResponseBody(ILogger? logger, string url, string body)");
    sb.AppendLine($"{indent}{{");
    sb.AppendLine($"{indent}{indent}if (logger != null)");
    sb.AppendLine($"{indent}{indent}{indent}LogTraceResponseBodyCore(logger, url, body);");
    sb.AppendLine($"{indent}}}");

    sb.AppendLine("}");

    return new GeneratedFile
    {
      FileName = "_HttpClientLog.g.cs",
      Content = sb.ToString(),
    };
  }

  private string GetIgnoreCondition()
  {
    return _serialization.IgnoreCondition switch
    {
      "WhenWritingNull" => "WhenWritingNull",
      "WhenWritingDefault" => "WhenWritingDefault",
      "Always" => "Always",
      _ => "Never"
    };
  }
}

/// <summary>
/// Container for all generated client code
/// </summary>
public class GeneratedClientCode
{
  public GeneratedFile MainClient { get; set; } = new();
  public List<GeneratedFile> ResourceClients { get; set; } = new();
  public List<GeneratedFile> ResourceInterfaces { get; set; } = new();
  public List<GeneratedFile> RequestFiles { get; set; } = new();
  public GeneratedFile ProjectFile { get; set; } = new();
}

/// <summary>
/// A generated file with name and content
/// </summary>
public class GeneratedFile
{
  public string FileName { get; set; } = string.Empty;
  public string Content { get; set; } = string.Empty;
}