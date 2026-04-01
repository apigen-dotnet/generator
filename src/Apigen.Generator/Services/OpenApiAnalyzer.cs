using System.Net.Http;
using Microsoft.OpenApi;
using StringCasing;
using Apigen.Generator.Extensions;
using Apigen.Generator.Models;
using System.Text.RegularExpressions;

namespace Apigen.Generator.Services;

/// <summary>
/// Analyzes OpenAPI specifications to extract patterns for client generation
/// </summary>
public class OpenApiAnalyzer
{
  private readonly TypeMapper _typeMapper;

  public OpenApiAnalyzer(List<TypeNameOverride>? typeNameOverrides = null, Dictionary<string, string>? namingOverrides = null)
  {
    _typeMapper = new TypeMapper(typeNameOverrides, namingOverrides);
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
      foreach (var schemePair in document.Components.SecuritySchemes)
      {
        AuthenticationScheme auth = new()
        {
          Name = schemePair.Key,
        };

        var scheme = (OpenApiSecurityScheme)schemePair.Value;

        if (scheme.Type == SecuritySchemeType.ApiKey)
        {
          auth.Type = AuthSchemeType.ApiKey;
          if (scheme.In == ParameterLocation.Cookie)
          {
            auth.In = AuthSchemeLocation.Cookie;
            auth.CookieName = scheme.Name;
          }
          else
          {
            auth.In = AuthSchemeLocation.Header;
            auth.HeaderName = scheme.Name;
          }
        }
        else if (scheme.Type == SecuritySchemeType.Http)
        {
          auth.Type = AuthSchemeType.Http;
          auth.In = AuthSchemeLocation.Header;
          if (string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase))
          {
            auth.HeaderName = "Authorization";
            auth.Scheme = HttpAuthScheme.Bearer;
          }
          else if (string.Equals(scheme.Scheme, "basic", StringComparison.OrdinalIgnoreCase))
          {
            auth.HeaderName = "Authorization";
            auth.Scheme = HttpAuthScheme.Basic;
          }
        }
        else if (scheme.Type == SecuritySchemeType.OAuth2)
        {
          auth.Type = AuthSchemeType.OAuth2;
          auth.In = AuthSchemeLocation.Header;
          auth.HeaderName = "Authorization";
          auth.Scheme = HttpAuthScheme.Bearer;
        }

        schemes.Add(auth);
      }

      // Keep spec order — no reason to re-sort
    }

    return schemes;
  }

  private AuthenticationScheme AnalyzeAuthentication(OpenApiDocument document)
  {
    AuthenticationScheme auth = new();

    if (document.Components?.SecuritySchemes?.Any() == true)
    {
      // Try to find an ApiKey or HTTP Bearer scheme
      var apiKeyScheme = document.Components.SecuritySchemes
        .FirstOrDefault(s => ((OpenApiSecurityScheme)s.Value).Type == SecuritySchemeType.ApiKey);

      var httpScheme = document.Components.SecuritySchemes
        .FirstOrDefault(s => ((OpenApiSecurityScheme)s.Value).Type == SecuritySchemeType.Http && string.Equals(((OpenApiSecurityScheme)s.Value).Scheme, "bearer", StringComparison.OrdinalIgnoreCase));

      // Prefer ApiKey schemes
      if (apiKeyScheme.Value != null)
      {
        var scheme = (OpenApiSecurityScheme)apiKeyScheme.Value;
        auth.Type = AuthSchemeType.ApiKey;
        auth.In = AuthSchemeLocation.Header;
        auth.HeaderName = scheme.Name;
      }
      // Fall back to HTTP Bearer
      else if (httpScheme.Value != null)
      {
        auth.Type = AuthSchemeType.Http;
        auth.In = AuthSchemeLocation.Header;
        auth.HeaderName = "Authorization";
        auth.Scheme = HttpAuthScheme.Bearer;
      }
      // Fall back to first scheme
      else
      {
        var scheme = (OpenApiSecurityScheme)document.Components.SecuritySchemes.First().Value;

        if (scheme.Type == SecuritySchemeType.ApiKey)
        {
          auth.Type = AuthSchemeType.ApiKey;
          auth.In = AuthSchemeLocation.Header;
          auth.HeaderName = scheme.Name;
        }
        else if (scheme.Type == SecuritySchemeType.Http && string.Equals(scheme.Scheme, "bearer", StringComparison.OrdinalIgnoreCase))
        {
          auth.Type = AuthSchemeType.Http;
          auth.In = AuthSchemeLocation.Header;
          auth.HeaderName = "Authorization";
          auth.Scheme = HttpAuthScheme.Bearer;
        }
      }
    }

    return auth;
  }

  private List<ResourceOperation> AnalyzeResources(OpenApiDocument document)
  {
    Dictionary<string, ResourceOperation> resources = new();

    foreach (var path in document.Paths)
    {
      if (path.Value is not OpenApiPathItem pathItem)
        continue;

      foreach (var operation in pathItem.Operations ?? Enumerable.Empty<KeyValuePair<HttpMethod, OpenApiOperation>>())
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
          Method = operation.Key.Method.ToUpperInvariant(),
          Path = path.Key,
          OperationId = operation.Value.OperationId ?? $"{operation.Key.Method.ToTitleCase()}{tag}",
          Summary = operation.Value.Summary ?? "",
          Type = DetermineOperationType(operation.Key, path.Key, tag),
          Parameters = AnalyzeParameters(operation.Value, path.Key, pathItem),
          RequestBodyType = GetRequestBodyType(operation.Value),
          RequestContentType = GetRequestContentType(operation.Value),
          ResponseType = GetResponseType(operation.Value),
          ResponseContentType = GetResponseContentType(operation.Value),
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
    HttpMethod method,
    string path,
    string resource)
  {
    string cleanPath = path.ToLowerInvariant();

    if (method == HttpMethod.Get && IsListEndpoint(cleanPath, resource)) return Models.OperationType.List;
    if (method == HttpMethod.Get && IsGetByIdEndpoint(cleanPath)) return Models.OperationType.GetById;
    if (method == HttpMethod.Post && IsBulkEndpoint(cleanPath)) return Models.OperationType.Bulk;
    if (method == HttpMethod.Post && IsCreateEndpoint(cleanPath, resource)) return Models.OperationType.Create;
    if (method == HttpMethod.Put && IsGetByIdEndpoint(cleanPath)) return Models.OperationType.Update;
    if (method == HttpMethod.Delete && IsGetByIdEndpoint(cleanPath)) return Models.OperationType.Delete;
    return Models.OperationType.Custom;
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
      foreach (var param in operation.Parameters)
      {
        string paramName = param.Name ?? "";
        parameters.Add(
          new ApiParameter
          {
            Name = paramName,
            Type = GetParameterType(param.Schema != null ? param.Schema.ResolveSchema() : null),
            Location = param.In?.ToString().ToLowerInvariant() ?? "query",
            Required = param.Required,
            Description = param.Description,
          });

        // Remove from path params set if explicitly defined
        if (param.In?.ToString().ToLowerInvariant() == "path")
        {
          pathParamNames.Remove(paramName);
        }
      }
    }

    // Add path-level parameters
    if (pathItem.Parameters != null)
    {
      foreach (var param in pathItem.Parameters)
      {
        string paramName = param.Name ?? "";
        // Check if not already added by operation
        if (!parameters.Any(p => p.Name == paramName))
        {
          parameters.Add(
            new ApiParameter
            {
              Name = paramName,
              Type = GetParameterType(param.Schema != null ? param.Schema.ResolveSchema() : null),
              Location = param.In?.ToString().ToLowerInvariant() ?? "query",
              Required = param.Required,
              Description = param.Description,
            });

          // Remove from path params set if explicitly defined
          if (param.In?.ToString().ToLowerInvariant() == "path")
          {
            pathParamNames.Remove(paramName);
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

    return schema.GetEffectiveType() switch
    {
      JsonSchemaType.Integer => "int",
      JsonSchemaType.Number => "decimal",
      JsonSchemaType.Boolean => "bool",
      JsonSchemaType.Array => "string[]",
      _ => "string",
    };
  }

  private string? GetRequestBodyType(OpenApiOperation operation)
  {
    var content = operation.RequestBody?.Content?.FirstOrDefault().Value;

    // Check for $ref BEFORE resolving — Reference.Id has the schema name
    string? refName = content?.Schema?.GetSchemaReferenceName();
    if (!string.IsNullOrEmpty(refName))
    {
      return _typeMapper.GetClassName(refName);
    }

    var schema = content?.Schema != null ? content.Schema.ResolveSchema() : null;

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
    var successResponse = operation.Responses?.FirstOrDefault(r => r.Key.StartsWith("2"));
    if (successResponse?.Key == "204")
    {
      return "void";
    }

    // Check if the response is binary (octet-stream, image/*, etc.)
    string responseContentType = GetResponseContentType(operation);
    if (IsBinaryContentType(responseContentType))
    {
      return "Stream";
    }

    var response = successResponse?.Value;
    var content = response?.Content?.FirstOrDefault().Value;

    // Check for $ref BEFORE resolving - Reference.Id has the schema name
    string? refName = content?.Schema?.GetSchemaReferenceName();
    if (!string.IsNullOrEmpty(refName))
    {
      return _typeMapper.GetClassName(refName);
    }

    var schema = content?.Schema != null ? content.Schema.ResolveSchema() : null;

    // Handle direct array responses (e.g., array of TasksView without wrapper)
    if (schema != null && schema.IsType(JsonSchemaType.Array) && schema.Items != null)
    {
      string? itemRefName = schema.Items.GetSchemaReferenceName();
      if (!string.IsNullOrEmpty(itemRefName))
      {
        string itemType = _typeMapper.GetClassName(itemRefName);
        return $"List<{itemType}>";
      }
    }

    // Handle wrapped responses like {data: Client[], meta: {}}
    if (schema?.Properties?.ContainsKey("data") == true)
    {
      var dataISchema = schema.Properties["data"];
      var dataSchema = dataISchema.ResolveSchema();
      if (dataSchema.IsType(JsonSchemaType.Array) && dataSchema.Items != null)
      {
        string? dataItemRefName = dataSchema.Items.GetSchemaReferenceName();
        if (!string.IsNullOrEmpty(dataItemRefName))
        {
          string itemType = _typeMapper.GetClassName(dataItemRefName);
          return $"{itemType}[]";
        }
      }

      string? dataRefName = dataISchema.GetSchemaReferenceName();
      if (!string.IsNullOrEmpty(dataRefName))
      {
        return _typeMapper.GetClassName(dataRefName);
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

  private string GetRequestContentType(OpenApiOperation operation)
  {
    string? contentType = operation.RequestBody?.Content?.Keys.FirstOrDefault();
    return contentType ?? "application/json";
  }

  private string GetResponseContentType(OpenApiOperation operation)
  {
    var successResponse = operation.Responses?.FirstOrDefault(r => r.Key.StartsWith("2"));
    string? contentType = successResponse?.Value?.Content?.Keys.FirstOrDefault();
    return contentType ?? "application/json";
  }

  private bool IsBinaryContentType(string contentType) =>
    contentType is "application/octet-stream"
      or "image/jpeg" or "image/png" or "image/gif" or "image/webp"
      or "audio/mpeg" or "video/mp4"
      || contentType.StartsWith("image/")
      || contentType.StartsWith("audio/")
      || contentType.StartsWith("video/");

  private ResponsePattern AnalyzeResponsePatterns(OpenApiDocument document)
  {
    ResponsePattern pattern = new();

    // Sample a few responses to detect patterns
    var responses = document.Paths
      .SelectMany(p => p.Value?.Operations ?? Enumerable.Empty<KeyValuePair<HttpMethod, OpenApiOperation>>())
      .SelectMany(o => o.Value.Responses ?? new OpenApiResponses())
      .Where(r => r.Key.StartsWith("2"))
      .Take(10); // Increased sample size for better analysis

    foreach (var response in responses)
    {
      var rawSchema = response.Value.Content?.FirstOrDefault().Value?.Schema;
      var schema = rawSchema != null ? rawSchema.ResolveSchema() : null;
      if (schema?.Properties?.ContainsKey("data") == true)
      {
        pattern.IsWrapped = true;
        pattern.DataProperty = "data";

        if (schema.Properties.ContainsKey("meta"))
        {
          pattern.MetaProperty = "meta";

          // Analyze the meta object properties with proper $ref resolution
          var metaSchema = schema.Properties["meta"].ResolveSchema();
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
      foreach (var metaProp in resolvedSchema.Properties)
      {
        // Use IOpenApiSchema overload to check $ref before resolution
        string propType = GetSchemaTypeWithRef(metaProp.Value, document);
        pattern.MetaProperties[metaProp.Key] = propType;
      }
    }
  }

  private OpenApiSchema? ResolveSchemaReference(OpenApiSchema schema, OpenApiDocument document)
  {
    // In OpenApi 3.x, schema.Id may be empty even for resolved references.
    // This method is only called on already-resolved schemas, so check Id as fallback.
    string? schemaName = schema.Id;
    if (!string.IsNullOrEmpty(schemaName) && document.Components?.Schemas?.ContainsKey(schemaName) == true)
    {
      return document.Components.Schemas[schemaName].ResolveSchema();
    }

    return schema;
  }

  private string GetSchemaTypeWithRef(OpenApiSchema schema, OpenApiDocument document)
  {
    // Handle $ref references
    if (!string.IsNullOrEmpty(schema.Id))
    {
      return schema.Id;
    }

    return GetSchemaType(schema);
  }

  /// <summary>
  /// Overload that accepts IOpenApiSchema to check for $ref before resolution
  /// </summary>
  private string GetSchemaTypeWithRef(IOpenApiSchema iSchema, OpenApiDocument document)
  {
    string? refName = iSchema.GetSchemaReferenceName();
    if (!string.IsNullOrEmpty(refName))
    {
      return refName;
    }
    return GetSchemaTypeWithRef(iSchema.ResolveSchema(), document);
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
          var schema = document.Components.Schemas[schemaName].ResolveSchema();
          Dictionary<string, string> properties = new();

          if (schema.Properties != null)
          {
            foreach (var prop in schema.Properties)
            {
              // Use IOpenApiSchema overload to check $ref before resolution
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
    return schema.GetEffectiveType() switch
    {
      JsonSchemaType.Integer => "int",
      JsonSchemaType.Number => "decimal",
      JsonSchemaType.Boolean => "bool",
      JsonSchemaType.Array => "string[]",
      _ => "string",
    };
  }

  private ParameterPattern AnalyzeParameterPatterns(OpenApiDocument document)
  {
    ParameterPattern pattern = new();

    // Look for common pagination/filtering parameters
    var allParams = document.Paths
      .SelectMany(p => p.Value?.Operations ?? Enumerable.Empty<KeyValuePair<HttpMethod, OpenApiOperation>>())
      .SelectMany(o => o.Value.Parameters ?? (IList<IOpenApiParameter>)new List<IOpenApiParameter>())
      .ToList();

    List<string> allParamNames = allParams
      .Where(p => p.Name != null)
      .Select(p => p.Name!.ToLowerInvariant())
      .Distinct()
      .ToList();

    // Collect actual pagination parameter names
    List<string> paginationParams = allParams
      .Where(p => p.Name != null && IsPaginationParameter(p.Name))
      .Select(p => p.Name!)
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
