using Apigen.Generator.Extensions;
using Apigen.Generator.Models;
using Microsoft.OpenApi;

namespace Apigen.Generator.Services;

/// <summary>
/// Analyzes OpenAPI specification to determine how schemas are used
/// </summary>
public class SchemaUsageAnalyzer
{
  private readonly OpenApiDocument _document;
  private readonly Dictionary<string, SchemaUsage> _usageMap = new();

  public SchemaUsageAnalyzer(OpenApiDocument document)
  {
    _document = document;
  }

  /// <summary>
  /// Analyzes the entire API specification and builds usage map
  /// </summary>
  public Dictionary<string, SchemaUsage> Analyze()
  {
    _usageMap.Clear();

    // Initialize usage entries for all schemas
    if (_document.Components?.Schemas != null)
    {
      foreach (var schema in _document.Components.Schemas)
      {
        _usageMap[schema.Key] = new SchemaUsage { SchemaName = schema.Key };
      }
    }

    // Analyze operations
    if (_document.Paths != null)
    {
      foreach (var path in _document.Paths)
      {
        foreach (var operation in path.Value?.Operations ?? Enumerable.Empty<KeyValuePair<HttpMethod, OpenApiOperation>>())
        {
          string operationId = operation.Value.OperationId ?? $"{operation.Key}_{path.Key}";
          string httpMethod = operation.Key.Method.ToUpperInvariant();
          AnalyzeOperation(operationId, httpMethod, operation.Value);
        }
      }
    }

    // Analyze schema references to build dependency graph
    if (_document.Components?.Schemas != null)
    {
      foreach (var schema in _document.Components.Schemas)
      {
        AnalyzeSchemaReferences(schema.Key, (OpenApiSchema)schema.Value);
      }
    }

    // Propagate readOnly/writeOnly flags up the dependency graph
    PropagatePropertyFlags();

    return _usageMap;
  }

  private void AnalyzeOperation(string operationId, string httpMethod, OpenApiOperation operation)
  {
    // Check request body
    if (operation.RequestBody?.Content != null)
    {
      foreach (var mediaType in operation.RequestBody.Content.Values)
      {
        string? schemaName = GetSchemaName(mediaType.Schema);
        if (schemaName != null && _usageMap.ContainsKey(schemaName))
        {
          _usageMap[schemaName].UsedInRequestBody.Add(operationId);
          _usageMap[schemaName].UsedInRequestMethods.Add(httpMethod);
        }
      }
    }

    // Check responses
    if (operation.Responses != null)
    {
      foreach (var response in operation.Responses.Values)
      {
        if (response.Content != null)
        {
          foreach (var mediaType in response.Content.Values)
          {
            string? schemaName = GetSchemaName(mediaType.Schema);
            if (schemaName != null && _usageMap.ContainsKey(schemaName))
            {
              _usageMap[schemaName].UsedInResponse.Add(operationId);
              _usageMap[schemaName].UsedInResponseMethods.Add(httpMethod);
            }
          }
        }
      }
    }
  }

  private void AnalyzeSchemaReferences(string schemaName, OpenApiSchema schema)
  {
    if (!_usageMap.ContainsKey(schemaName))
    {
      return;
    }

    SchemaUsage usage = _usageMap[schemaName];

    // Check for readOnly/writeOnly properties at this level
    if (schema.Properties != null)
    {
      foreach (var property in schema.Properties)
      {
        if (property.Value.ReadOnly)
        {
          usage.HasReadOnlyProperties = true;
        }

        if (property.Value.WriteOnly)
        {
          usage.HasWriteOnlyProperties = true;
        }

        // Track nested references
        string? referencedSchema = GetSchemaName(property.Value);
        if (referencedSchema != null && _usageMap.ContainsKey(referencedSchema))
        {
          usage.References.Add(referencedSchema);
          _usageMap[referencedSchema].ReferencedBy.Add(schemaName);
        }
      }
    }

    // Check array items
    if (schema.Items != null)
    {
      string? itemSchemaName = GetSchemaName(schema.Items);
      if (itemSchemaName != null && _usageMap.ContainsKey(itemSchemaName))
      {
        usage.References.Add(itemSchemaName);
        _usageMap[itemSchemaName].ReferencedBy.Add(schemaName);
      }
    }

    // Check allOf, oneOf, anyOf
    foreach (var subSchema in schema.AllOf ?? Enumerable.Empty<IOpenApiSchema>())
    {
      string? refSchema = GetSchemaName(subSchema);
      if (refSchema != null && _usageMap.ContainsKey(refSchema))
      {
        usage.References.Add(refSchema);
        _usageMap[refSchema].ReferencedBy.Add(schemaName);
      }
    }
  }

  /// <summary>
  /// Propagates readOnly/writeOnly flags up the dependency graph
  /// If a schema references another schema with readOnly, the parent also has readOnly
  /// </summary>
  private void PropagatePropertyFlags()
  {
    bool changed;
    int maxIterations = 100; // Prevent infinite loops
    int iteration = 0;

    do
    {
      changed = false;
      iteration++;

      foreach (SchemaUsage usage in _usageMap.Values)
      {
        foreach (string referencedSchema in usage.References)
        {
          if (_usageMap.TryGetValue(referencedSchema, out SchemaUsage? referenced))
          {
            if (referenced.HasReadOnlyProperties && !usage.HasReadOnlyProperties)
            {
              usage.HasReadOnlyProperties = true;
              changed = true;
            }

            if (referenced.HasWriteOnlyProperties && !usage.HasWriteOnlyProperties)
            {
              usage.HasWriteOnlyProperties = true;
              changed = true;
            }
          }
        }
      }
    } while (changed && iteration < maxIterations);
  }

  private string? GetSchemaName(IOpenApiSchema? schema)
  {
    return schema?.GetSchemaReferenceName();
  }

  /// <summary>
  /// Gets the usage information for a specific schema
  /// </summary>
  public SchemaUsage? GetUsage(string schemaName)
  {
    return _usageMap.TryGetValue(schemaName, out SchemaUsage? usage) ? usage : null;
  }
}
