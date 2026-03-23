using Microsoft.OpenApi.Models;
using Apigen.Generator.Models;
using System.Text.RegularExpressions;
using OperationType = Microsoft.OpenApi.Models.OperationType;

namespace Apigen.Generator.Services;

/// <summary>
/// Analyzes OpenAPI specifications to extract patterns for client generation
/// </summary>
public class OpenApiAnalyzer
{
  private readonly TypeMapper _typeMapper;

  public OpenApiAnalyzer(List<TypeNameOverride>? typeNameOverrides = null)
  {
    _typeMapper = new TypeMapper(typeNameOverrides);
  }

  /// <summary>
  /// Analyze an OpenAPI document to extract client generation information
  /// </summary>
  public OpenApiAnalysis Analyze(OpenApiDocument document)
  {
    List<AuthenticationScheme> allAuthSchemes = AnalyzeAllAuthenticationSchemes(document);
    AuthenticationScheme primaryAuth = allAuthSchemes.FirstOrDefault() ?? new AuthenticationScheme();

    OpenApiAnalysis analysis = new()
    {
      BaseUrl = GetBaseUrl(document),
      Authentication = primaryAuth,
      AuthenticationSchemes = allAuthSchemes,
      Resources = AnalyzeResources(document),
      ResponsePattern = AnalyzeResponsePatterns(document),
      ParameterPattern = AnalyzeParameterPatterns(document),
    };

    return analysis;
  }

  private string GetBaseUrl(OpenApiDocument document)
  {
    return document.Servers?.FirstOrDefault()?.Url ?? "https://localhost";
  }

  private List<AuthenticationScheme> AnalyzeAllAuthenticationSchemes(OpenApiDocument document)
  {
    List<AuthenticationScheme> schemes = new();

    if (document.Components?.SecuritySchemes?.Any() == true)
    {
      foreach (KeyValuePair<string, OpenApiSecurityScheme> schemePair in document.Components.SecuritySchemes)
      {
        AuthenticationScheme auth = new()
        {
          Name = schemePair.Key,
        };

        OpenApiSecurityScheme scheme = schemePair.Value;
        auth.Type = scheme.Type.ToString().ToLowerInvariant();

        if (scheme.Type == SecuritySchemeType.ApiKey)
        {
          auth.HeaderName = scheme.Name;
        }
        else if (scheme.Type == SecuritySchemeType.Http)
        {
          if (scheme.Scheme == "bearer")
          {
            auth.HeaderName = "Authorization";
            auth.Scheme = "Bearer";
          }
          else if (scheme.Scheme == "basic")
          {
            auth.HeaderName = "Authorization";
            auth.Scheme = "Basic";
          }
        }

        schemes.Add(auth);
      }

      // Sort: prefer ApiKey/Bearer, then Basic, then others
      schemes = schemes.OrderByDescending(s =>
      {
        if (s.Type == "apikey") return 3;
        if (s.Scheme == "Bearer") return 2;
        if (s.Scheme == "Basic") return 1;
        return 0;
      }).ToList();
    }

    return schemes;
  }

  private AuthenticationScheme AnalyzeAuthentication(OpenApiDocument document)
  {
    AuthenticationScheme auth = new();

    if (document.Components?.SecuritySchemes?.Any() == true)
    {
      // Try to find an ApiKey or HTTP Bearer scheme
      KeyValuePair<string, OpenApiSecurityScheme>? apiKeyScheme = document.Components.SecuritySchemes
        .FirstOrDefault(s => s.Value.Type == SecuritySchemeType.ApiKey);

      KeyValuePair<string, OpenApiSecurityScheme>? httpScheme = document.Components.SecuritySchemes
        .FirstOrDefault(s => s.Value.Type == SecuritySchemeType.Http && s.Value.Scheme == "bearer");

      // Prefer ApiKey schemes
      if (apiKeyScheme != null && apiKeyScheme.Value.Value != null)
      {
        OpenApiSecurityScheme scheme = apiKeyScheme.Value.Value;
        auth.Type = scheme.Type.ToString().ToLowerInvariant();
        auth.HeaderName = scheme.Name;
      }
      // Fall back to HTTP Bearer
      else if (httpScheme != null && httpScheme.Value.Value != null)
      {
        auth.Type = "http";
        auth.HeaderName = "Authorization";
        auth.Scheme = "Bearer";
      }
      // Fall back to first scheme
      else
      {
        OpenApiSecurityScheme scheme = document.Components.SecuritySchemes.First().Value;
        auth.Type = scheme.Type.ToString().ToLowerInvariant();

        if (scheme.Type == SecuritySchemeType.ApiKey)
        {
          auth.HeaderName = scheme.Name;
        }
        else if (scheme.Type == SecuritySchemeType.Http && scheme.Scheme == "bearer")
        {
          auth.HeaderName = "Authorization";
          auth.Scheme = "Bearer";
        }
      }
    }

    return auth;
  }

  private List<ResourceOperation> AnalyzeResources(OpenApiDocument document)
  {
    Dictionary<string, ResourceOperation> resources = new();

    foreach (KeyValuePair<string, OpenApiPathItem> path in document.Paths)
    {
      foreach (KeyValuePair<OperationType, OpenApiOperation> operation in path.Value.Operations)
      {
        string? tag = operation.Value.Tags?.FirstOrDefault()?.Name;
        if (string.IsNullOrEmpty(tag))
        {
          continue;
        }

        // Use tag as key for deduplication
        if (!resources.ContainsKey(tag))
        {
          resources[tag] = new ResourceOperation
          {
            Name = tag,
            Tag = tag,
            ModelName = InferModelName(tag),
          };
        }

        ApiOperation apiOperation = new()
        {
          Method = operation.Key.ToString().ToUpperInvariant(),
          Path = path.Key,
          OperationId = operation.Value.OperationId ?? $"{operation.Key}{tag}",
          Summary = operation.Value.Summary ?? "",
          Type = DetermineOperationType(operation.Key, path.Key, tag),
          Parameters = AnalyzeParameters(operation.Value, path.Key, path.Value),
          RequestBodyType = GetRequestBodyType(operation.Value),
          ResponseType = GetResponseType(operation.Value),
        };

        resources[tag].Operations.Add(apiOperation);
      }
    }

    return resources.Values.ToList();
  }

  private string? InferModelName(string tag)
  {
    // Convert plural resource names to singular model names
    if (tag.EndsWith("ies"))
    {
      return tag.Substring(0, tag.Length - 3) + "y"; // activities -> activity
    }

    if (tag.EndsWith("uses"))
    {
      return tag.Substring(0, tag.Length - 2); // statuses -> status
    }

    if (tag.EndsWith("sses"))
    {
      return tag.Substring(0, tag.Length - 2); // addresses -> address
    }

    if (tag.EndsWith("ss"))
    {
      return tag; // address -> address (no change)
    }

    if (tag.EndsWith("s"))
    {
      return tag.Substring(0, tag.Length - 1); // clients -> client
    }

    return tag;
  }

  private Models.OperationType DetermineOperationType(
    OperationType method,
    string path,
    string resource)
  {
    string cleanPath = path.ToLowerInvariant();

    return method switch
    {
      OperationType.Get when IsListEndpoint(cleanPath, resource) => Models.OperationType.List,
      OperationType.Get when IsGetByIdEndpoint(cleanPath) => Models.OperationType.GetById,
      OperationType.Post when IsBulkEndpoint(cleanPath) => Models.OperationType.Bulk,
      OperationType.Post when IsCreateEndpoint(cleanPath, resource) => Models.OperationType
        .Create,
      OperationType.Put when IsGetByIdEndpoint(cleanPath) => Models.OperationType.Update,
      OperationType.Delete when IsGetByIdEndpoint(cleanPath) => Models.OperationType.Delete,
      _ => Models.OperationType.Custom,
    };
  }

  private bool IsListEndpoint(string path, string resource)
  {
    return path == $"/api/v1/{resource}" || path.EndsWith($"/{resource}");
  }

  private bool IsGetByIdEndpoint(string path)
  {
    return Regex.IsMatch(path, @"/\{[^}]+\}$"); // ends with /{id}
  }

  private bool IsBulkEndpoint(string path)
  {
    return path.Contains("/bulk") || path.Contains("/batch");
  }

  private bool IsCreateEndpoint(string path, string resource)
  {
    return path == $"/api/v1/{resource}" || path.EndsWith($"/{resource}");
  }

  private List<ApiParameter> AnalyzeParameters(OpenApiOperation operation, string pathTemplate, OpenApiPathItem pathItem)
  {
    List<ApiParameter> parameters = new();

    // First, extract path parameters from the path template
    System.Text.RegularExpressions.MatchCollection pathParams = System.Text.RegularExpressions.Regex.Matches(pathTemplate, @"\{([^}]+)\}");
    HashSet<string> pathParamNames = new HashSet<string>();
    foreach (System.Text.RegularExpressions.Match match in pathParams)
    {
      pathParamNames.Add(match.Groups[1].Value);
    }

    // Add operation-level parameters
    if (operation.Parameters != null)
    {
      foreach (OpenApiParameter? param in operation.Parameters)
      {
        parameters.Add(
          new ApiParameter
          {
            Name = param.Name,
            Type = GetParameterType(param.Schema),
            Location = param.In?.ToString().ToLowerInvariant() ?? "query",
            Required = param.Required,
            Description = param.Description,
          });

        // Remove from path params set if explicitly defined
        if (param.In?.ToString().ToLowerInvariant() == "path")
        {
          pathParamNames.Remove(param.Name);
        }
      }
    }

    // Add path-level parameters
    if (pathItem.Parameters != null)
    {
      foreach (OpenApiParameter? param in pathItem.Parameters)
      {
        // Check if not already added by operation
        if (!parameters.Any(p => p.Name == param.Name))
        {
          parameters.Add(
            new ApiParameter
            {
              Name = param.Name,
              Type = GetParameterType(param.Schema),
              Location = param.In?.ToString().ToLowerInvariant() ?? "query",
              Required = param.Required,
              Description = param.Description,
            });

          // Remove from path params set if explicitly defined
          if (param.In?.ToString().ToLowerInvariant() == "path")
          {
            pathParamNames.Remove(param.Name);
          }
        }
      }
    }

    // Add any remaining path parameters that weren't explicitly defined in the spec
    // This handles malformed OpenAPI specs that have path parameters in URL but not in parameters list
    foreach (string paramName in pathParamNames)
    {
      parameters.Add(
        new ApiParameter
        {
          Name = paramName,
          Type = "string",  // Default to string since we don't have schema info
          Location = "path",
          Required = true,  // Path parameters are always required
          Description = $"Path parameter: {paramName}",
        });
    }

    return parameters;
  }

  private string GetParameterType(OpenApiSchema? schema)
  {
    if (schema == null)
    {
      return "string";
    }

    return schema.Type switch
    {
      "integer" => "int",
      "number" => "decimal",
      "boolean" => "bool",
      "array" => "string[]",
      _ => "string",
    };
  }

  private string? GetRequestBodyType(OpenApiOperation operation)
  {
    OpenApiMediaType? content = operation.RequestBody?.Content?.FirstOrDefault().Value;
    OpenApiSchema? schema = content?.Schema;

    if (schema?.Reference != null)
    {
      // Normalize the type name to match how model classes are generated
      return _typeMapper.GetClassName(schema.Reference.Id);
    }

    // Handle inline schemas (generate request models from inline body schemas)
    if (schema?.Properties != null && schema.Properties.Any())
    {
      string? operationId = operation.OperationId;
      if (!string.IsNullOrEmpty(operationId))
      {
        // Use a more generic approach: convert operationId to PascalCase + "Request"
        // Use GetClassName for consistent normalization
        string modelName = _typeMapper.GetClassName(operationId) + "Request";
        return modelName;
      }
    }

    return null;
  }

  private string ToPascalCase(string input)
  {
    if (string.IsNullOrEmpty(input))
    {
      return input;
    }

    // Handle camelCase or snake_case to PascalCase
    string[] parts = input.Split('_', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length > 1)
    {
      // snake_case: post_login -> PostLogin
      return string.Concat(
        parts.Select(part =>
                       char.ToUpperInvariant(part[0]) + part.Substring(1).ToLowerInvariant()));
    }
    else
    {
      // camelCase: postLogin -> PostLogin
      return char.ToUpperInvariant(input[0]) + input.Substring(1);
    }
  }

  private string? GetResponseType(OpenApiOperation operation)
  {
    // Check if this is a 204 No Content response
    KeyValuePair<string, OpenApiResponse>? successResponse = operation.Responses?.FirstOrDefault(r => r.Key.StartsWith("2"));
    if (successResponse?.Key == "204")
    {
      // 204 No Content should return void (Task with no result)
      return "void";
    }

    OpenApiResponse? response = successResponse?.Value;
    OpenApiMediaType? content = response?.Content?.FirstOrDefault().Value;
    OpenApiSchema? schema = content?.Schema;

    if (schema?.Reference != null)
    {
      // Normalize the type name to match how model classes are generated
      return _typeMapper.GetClassName(schema.Reference.Id);
    }

    // Handle direct array responses (e.g., array of TasksView without wrapper)
    if (schema?.Type == "array" && schema.Items?.Reference != null)
    {
      string itemType = _typeMapper.GetClassName(schema.Items.Reference.Id);
      return $"List<{itemType}>";
    }

    // Handle wrapped responses like {data: Client[], meta: {}}
    if (schema?.Properties?.ContainsKey("data") == true)
    {
      OpenApiSchema? dataSchema = schema.Properties["data"];
      if (dataSchema.Type == "array" && dataSchema.Items?.Reference != null)
      {
        // Normalize the type name for array items
        string itemType = _typeMapper.GetClassName(dataSchema.Items.Reference.Id);
        return $"{itemType}[]";
      }

      if (dataSchema.Reference != null)
      {
        // Normalize the type name for data property
        return _typeMapper.GetClassName(dataSchema.Reference.Id);
      }
    }

    // If there's no content or schema but it's not 204, check if content-type is JSON
    if (schema == null && content == null)
    {
      // No response body defined - return void
      return "void";
    }

    // Unknown schema with JSON content - use JsonElement so caller can access dynamic data
    if (schema != null && response?.Content?.Keys.Any(k => k.Contains("json")) == true)
    {
      return "JsonElement";
    }

    return null;
  }

  private ResponsePattern AnalyzeResponsePatterns(OpenApiDocument document)
  {
    ResponsePattern pattern = new();

    // Sample a few responses to detect patterns
    IEnumerable<KeyValuePair<string, OpenApiResponse>> responses = document.Paths
      .SelectMany(p => p.Value.Operations)
      .SelectMany(o => o.Value.Responses)
      .Where(r => r.Key.StartsWith("2"))
      .Take(10); // Increased sample size for better analysis

    foreach (KeyValuePair<string, OpenApiResponse> response in responses)
    {
      OpenApiSchema? schema = response.Value.Content?.FirstOrDefault().Value?.Schema;
      if (schema?.Properties?.ContainsKey("data") == true)
      {
        pattern.IsWrapped = true;
        pattern.DataProperty = "data";

        if (schema.Properties.ContainsKey("meta"))
        {
          pattern.MetaProperty = "meta";

          // Analyze the meta object properties with proper $ref resolution
          OpenApiSchema? metaSchema = schema.Properties["meta"];
          AnalyzeMetaSchema(metaSchema, pattern, document);
        }
      }
    }

    // Collect detailed information about referenced schemas
    CollectReferencedSchemas(pattern, document);

    return pattern;
  }

  private void AnalyzeMetaSchema(OpenApiSchema metaSchema, ResponsePattern pattern, OpenApiDocument document)
  {
    // Resolve $ref if present
    OpenApiSchema? resolvedSchema = ResolveSchemaReference(metaSchema, document);

    if (resolvedSchema?.Properties != null)
    {
      foreach (KeyValuePair<string, OpenApiSchema> metaProp in resolvedSchema.Properties)
      {
        string propType = GetSchemaTypeWithRef(metaProp.Value, document);
        pattern.MetaProperties[metaProp.Key] = propType;
      }
    }
  }

  private OpenApiSchema? ResolveSchemaReference(OpenApiSchema schema, OpenApiDocument document)
  {
    if (schema.Reference != null)
    {
      // Extract schema name from reference like "#/components/schemas/Meta"
      string? schemaName = schema.Reference.Id;
      if (document.Components?.Schemas?.ContainsKey(schemaName) == true)
      {
        return document.Components.Schemas[schemaName];
      }
    }

    return schema;
  }

  private string GetSchemaTypeWithRef(OpenApiSchema schema, OpenApiDocument document)
  {
    // Handle $ref references
    if (schema.Reference != null)
    {
      string? schemaName = schema.Reference.Id;
      // For nested objects, return the schema name as the type
      return schemaName;
    }

    return GetSchemaType(schema);
  }

  private void CollectReferencedSchemas(ResponsePattern pattern, OpenApiDocument document)
  {
    foreach (KeyValuePair<string, string> metaProp in pattern.MetaProperties)
    {
      // If the property type is not a primitive, it's likely a referenced schema
      if (!IsPrimitiveType(metaProp.Value) && metaProp.Value != "string")
      {
        string schemaName = metaProp.Value;
        if (document.Components?.Schemas?.ContainsKey(schemaName) == true)
        {
          OpenApiSchema? schema = document.Components.Schemas[schemaName];
          Dictionary<string, string> properties = new();

          if (schema.Properties != null)
          {
            foreach (KeyValuePair<string, OpenApiSchema> prop in schema.Properties)
            {
              properties[prop.Key] = GetSchemaTypeWithRef(prop.Value, document);
            }
          }

          pattern.ReferencedSchemas[schemaName] = properties;
        }
      }
    }
  }

  private bool IsPrimitiveType(string type)
  {
    return type switch
    {
      "int" or "decimal" or "bool" or "double" or "float" or "long" or "string" => true,
      _ => false,
    };
  }

  private string GetSchemaType(OpenApiSchema schema)
  {
    return schema.Type switch
    {
      "integer" => "int",
      "number" => "decimal",
      "boolean" => "bool",
      "array" => "string[]",
      _ => "string",
    };
  }

  private ParameterPattern AnalyzeParameterPatterns(OpenApiDocument document)
  {
    ParameterPattern pattern = new();

    // Look for common pagination/filtering parameters
    List<OpenApiParameter> allParams = document.Paths
      .SelectMany(p => p.Value.Operations)
      .SelectMany(o => o.Value.Parameters ?? new List<OpenApiParameter>())
      .ToList();

    List<string> allParamNames = allParams.Select(p => p.Name.ToLowerInvariant()).Distinct().ToList();

    // Collect actual pagination parameter names
    List<string> paginationParams = allParams
      .Where(p => IsPaginationParameter(p.Name))
      .Select(p => p.Name)
      .Distinct()
      .ToList();

    pattern.PaginationParameters = paginationParams;
    pattern.SupportsPagination = paginationParams.Any();
    pattern.SupportsSorting = allParamNames.Any(p => p == "sort" || p == "order");
    pattern.SupportsFiltering = allParamNames.Count() > 5; // Heuristic

    return pattern;
  }

  private bool IsPaginationParameter(string paramName)
  {
    string lower = paramName.ToLowerInvariant();
    return lower == "page" || lower == "per_page" || lower == "limit" ||
           lower == "offset" || lower == "size" || lower == "page_size" ||
           lower == "pagesize" || lower == "perpage";
  }
}