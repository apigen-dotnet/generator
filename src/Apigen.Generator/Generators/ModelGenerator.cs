using System.Text;
using System.Text.Json.Nodes;
using StringCasing;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;
using Apigen.Generator.Extensions;
using Apigen.Generator.Models;
using Apigen.Generator.Services;

namespace Apigen.Generator.Generators;

/// <summary>
/// Represents the purpose of a model being generated
/// </summary>
public enum ModelPurpose
{
  /// <summary>
  /// POST request model - all required fields enforced
  /// </summary>
  CreateRequest,

  /// <summary>
  /// PUT request model - all required fields enforced
  /// </summary>
  UpdateRequest,

  /// <summary>
  /// PATCH request model - all fields optional/nullable
  /// </summary>
  PatchRequest,

  /// <summary>
  /// Response model - all required fields enforced
  /// </summary>
  Response
}

public class ModelGenerator
{
  private readonly TypeMapper _typeMapper;
  private readonly GeneratorOptions _options;
  private readonly CodeFormattingOptions _formatting;
  private readonly GeneratorConfiguration _config;
  private readonly HashSet<string> _generatedModels;
  private readonly HashSet<string> _generatedEnums;
  private OpenApiDocument? _currentDocument;

  public ModelGenerator(GeneratorOptions options, CodeFormattingOptions? formatting = null)
  {
    _options = options;
    _formatting = formatting ?? new CodeFormattingOptions();
    _config = new GeneratorConfiguration
    {
      Formatting = _formatting,
      PropertyOverrides = new List<PropertyOverride>(),
      JsonConverters = new List<JsonConverterConfig>(),
      GlobalUsings = new List<string>(),
    };
    _typeMapper = new TypeMapper(_config.TypeNameOverrides, _config.Naming.Overrides);
    _generatedModels = new HashSet<string>();
    _generatedEnums = new HashSet<string>();
  }

  public ModelGenerator(GeneratorOptions options, GeneratorConfiguration config)
  {
    _options = options;
    _formatting = config.Formatting;
    _config = config;
    _typeMapper = new TypeMapper(config.TypeNameOverrides, config.Naming.Overrides);
    _generatedModels = new HashSet<string>();
    _generatedEnums = new HashSet<string>();
  }

  public async Task<Dictionary<string, ModelGenerationDecision>?> GenerateModelsAsync(OpenApiDocument document)
  {
    _currentDocument = document;

    string outputDir = Path.Combine(_options.OutputPath, _options.ProjectName);

    if (Directory.Exists(outputDir))
    {
      Directory.Delete(outputDir, true);
    }

    Directory.CreateDirectory(outputDir);

    await GenerateProjectFileAsync(outputDir);

    // Generate JSON converters first
    await GenerateJsonConvertersAsync(outputDir);

    // Generate enhanced enum support files
    await GenerateEnhancedEnumSupportAsync(outputDir);

    // Generate enums
    await GenerateEnumsAsync(outputDir);

    // NEW: Analyze schema usage and make generation decisions
    Dictionary<string, ModelGenerationDecision>? decisions = null;
    SchemaVariantGenerator? variantGenerator = null;

    if (_options.ModelGeneration.Strategy != ModelGenerationStrategy.SingleModel && document.Components?.Schemas != null)
    {
      Console.WriteLine("Analyzing schema usage patterns...");
      SchemaUsageAnalyzer analyzer = new SchemaUsageAnalyzer(document);
      Dictionary<string, SchemaUsage> usageMap = analyzer.Analyze();

      Console.WriteLine("Generating schema variants...");
      variantGenerator = new SchemaVariantGenerator(document, usageMap);
      Dictionary<string, Dictionary<SchemaVariantType, SchemaVariant>> variants = variantGenerator.GenerateVariants();

      Console.WriteLine("Making model generation decisions...");
      ModelDeduplicator deduplicator = new ModelDeduplicator(usageMap, variants);
      decisions = deduplicator.MakeDecisions();

      // Cross-schema deduplication: eliminate identical schemas
      deduplicator.DeduplicateAcrossSchemas();

      // Log decisions for debugging
      int splitCount = decisions.Values.Count(d => d.ShouldSplit);
      int skippedCount = decisions.Values.Count(d => d.SkipGeneration);
      int unifiedCount = decisions.Count - splitCount - skippedCount;
      Console.WriteLine($"Decisions: {splitCount} schemas will split, {unifiedCount} will remain unified, {skippedCount} duplicates skipped");
    }

    if (document.Components?.Schemas != null)
    {
      foreach (var schema in document.Components.Schemas)
      {
        // Skip schemas that are null-only enums (e.g. {"enum": [null]}) - they represent nullable markers, not real types
        if (IsNullOnlySchema((OpenApiSchema)schema.Value))
        {
          Console.WriteLine($"  Skipping null-only schema: {schema.Key}");
          continue;
        }

        await GenerateModelClassAsync(schema.Key, (OpenApiSchema)schema.Value, outputDir, decisions, variantGenerator);
      }
    }

    // Generate request models from inline request body schemas
    await GenerateRequestModelsAsync(document, outputDir);

    // Generate .ToRequest() extension methods if enabled
    if (_options.ModelGeneration.GenerateToRequestExtensions && decisions != null)
    {
      await GenerateToRequestExtensionsAsync(outputDir, decisions, document);
    }

    Console.WriteLine($"Generated {_generatedModels.Count} model classes in {outputDir}");

    return decisions;
  }

  private async Task GenerateProjectFileAsync(string outputDir)
  {
    string projectContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>{_options.TargetFramework}</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>{(_options.GenerateNullableReferenceTypes ? "enable" : "disable")}</Nullable>
  </PropertyGroup>

{(_options.GenerateDataAnnotations ? @"  <ItemGroup>
    <PackageReference Include=""System.ComponentModel.Annotations"" Version=""5.0.0"" />
  </ItemGroup>" : "")}

</Project>";

    string projectPath = Path.Combine(outputDir, $"{_options.ProjectName}.csproj");
    await File.WriteAllTextAsync(projectPath, projectContent);
  }

  private async Task GenerateModelClassAsync(
    string schemaName,
    OpenApiSchema schema,
    string outputDir,
    Dictionary<string, ModelGenerationDecision>? decisions = null,
    SchemaVariantGenerator? variantGenerator = null)
  {
    // Apply type name overrides FIRST (before splitting)
    TypeNameOverride? typeOverride = _config.TypeNameOverrides.FirstOrDefault(o => o.Matches(schemaName));
    string effectiveSchemaName = typeOverride != null ? typeOverride.Apply(schemaName) : schemaName;

    // Check if this schema should be split into Request/Response models
    if (decisions != null && decisions.TryGetValue(schemaName, out ModelGenerationDecision? decision))
    {
      // Skip duplicates identified by cross-schema deduplication
      if (decision.SkipGeneration)
      {
        return;
      }

      if (decision.ShouldGenerateMethodSpecificModels)
      {
        // Generate method-specific models (Create/Update/Patch)
        if (decision.CreateModelName != null)
        {
          await GenerateMethodSpecificModelAsync(decision.CreateModelName, effectiveSchemaName, schema, outputDir, variantGenerator, ModelPurpose.CreateRequest);
        }

        if (decision.UpdateModelName != null)
        {
          await GenerateMethodSpecificModelAsync(decision.UpdateModelName, effectiveSchemaName, schema, outputDir, variantGenerator, ModelPurpose.UpdateRequest);
        }

        if (decision.PatchModelName != null)
        {
          await GenerateMethodSpecificModelAsync(decision.PatchModelName, effectiveSchemaName, schema, outputDir, variantGenerator, ModelPurpose.PatchRequest);
        }

        // Generate response model if used in responses
        if (decision.ResponseModelName != null)
        {
          await GenerateResponseModelAsync(effectiveSchemaName, schema, outputDir, variantGenerator);
        }

        return;
      }
      else if (decision.ShouldSplit)
      {
        // Generate both Request and Response models using the effective (renamed) schema name
        await GenerateRequestModelAsync(effectiveSchemaName, schema, outputDir, variantGenerator);
        await GenerateResponseModelAsync(effectiveSchemaName, schema, outputDir, variantGenerator);
        return;
      }
    }

    // Check if this is an enum BEFORE generating class structure
    if (schema.Enum != null && schema.Enum.Count > 0 && (schema.IsType(JsonSchemaType.String) || schema.IsType(JsonSchemaType.Integer)))
    {
      // Skip enum generation here if enhanced enum generation is enabled
      // The enhanced generator will handle it later in GenerateEnumsAsync
      if (_config.EnumGeneration.GenerationMode == EnumGenerationMode.Raw)
      {
        // Only use simple enum generation if explicitly set to Raw mode
        await GenerateEnumAsync(effectiveSchemaName, schema, outputDir);
      }
      return;
    }

    // Generate single unified model (legacy behavior)
    if (_generatedModels.Contains(effectiveSchemaName))
    {
      return;
    }

    _generatedModels.Add(effectiveSchemaName);

    // Continue with effective schema name
    string originalClassName = schemaName;

    // TypeMapper will apply overrides again, so just use effectiveSchemaName
    string className = _typeMapper.GetClassName(effectiveSchemaName);
    StringBuilder sb = new();

    // Add header if enabled
    if (_config.Header.IncludeHeader)
    {
      string header = _config.Header.GenerateHeader(
        _options.InputPath,
        _currentDocument?.Info?.Title,
        _currentDocument?.Info?.Version,
        _config
      );
      sb.Append(header);
    }

    // Collect all using statements and deduplicate
    HashSet<string> allUsings = new()
    {
      "System",
      "System.Collections.Generic",
    };

    if (_options.GenerateDataAnnotations)
    {
      allUsings.Add("System.ComponentModel.DataAnnotations");
    }

    // Add global usings from config
    foreach (string globalUsing in _config.GlobalUsings)
    {
      allUsings.Add(globalUsing);
    }

    // Add required usings from property overrides
    foreach (PropertyOverride propertyOverride in _config.PropertyOverrides)
    {
      foreach (string reqUsing in propertyOverride.RequiredUsings)
      {
        allUsings.Add(reqUsing);
      }
    }

    foreach (JsonConverterConfig converterConfig in _config.JsonConverters)
    {
      foreach (string reqUsing in converterConfig.RequiredUsings)
      {
        allUsings.Add(reqUsing);
      }
    }

    // Write deduplicated usings in sorted order
    foreach (string usingStatement in allUsings.OrderBy(u => u))
    {
      sb.AppendLine($"using {usingStatement};");
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

    if (!string.IsNullOrEmpty(schema.Description) || !string.IsNullOrEmpty(originalClassName))
    {
      sb.AppendLine("/// <summary>");

      // Always add original schema name and path
      sb.AppendLine($"/// {originalClassName} from OpenAPI schema.");
      sb.AppendLine($"/// Schema path: #/components/schemas/{schemaName}");

      // Add separator if there's also a description
      if (!string.IsNullOrEmpty(schema.Description))
      {
        sb.AppendLine("/// ");
      }

      // Add schema description
      if (!string.IsNullOrEmpty(schema.Description))
      {
        foreach (string line in schema.Description.Split('\n'))
        {
          sb.AppendLine($"/// {SanitizeXmlDocumentation(line.Trim())}");
        }
      }

      sb.AppendLine("/// </summary>");
    }

    sb.AppendLine($"public class {className}");
    sb.AppendLine("{");

    // Merge properties from allOf schemas
    Dictionary<string, OpenApiSchema> mergedProperties = new();
    HashSet<string> mergedRequired = new();

    // First, collect properties from allOf schemas
    if (schema.AllOf != null && schema.AllOf.Any())
    {
      foreach (var allOfSchema in schema.AllOf)
      {
        OpenApiSchema resolvedSchema = ResolveSchemaReference((OpenApiSchema)allOfSchema);

        // Merge properties
        if (resolvedSchema.Properties != null)
        {
          foreach (var prop in resolvedSchema.Properties)
          {
            mergedProperties[prop.Key] = (OpenApiSchema)prop.Value;
          }
        }

        // Merge required fields
        if (resolvedSchema.Required != null)
        {
          foreach (string req in resolvedSchema.Required)
          {
            mergedRequired.Add(req);
          }
        }
      }
    }

    // Then, add/override with schema's own properties (takes precedence)
    if (schema.Properties != null)
    {
      foreach (var prop in schema.Properties)
      {
        mergedProperties[prop.Key] = (OpenApiSchema)prop.Value;
      }
    }

    // Add schema's own required fields
    if (schema.Required != null)
    {
      foreach (string req in schema.Required)
      {
        mergedRequired.Add(req);
      }
    }

    // Generate properties from merged collection
    if (mergedProperties.Count > 0)
    {
      bool isFirst = true;
      foreach (KeyValuePair<string, OpenApiSchema> property in mergedProperties)
      {
        if (!isFirst)
        {
          sb.AppendLine();
        }

        GenerateProperty(
          sb,
          property.Key,
          property.Value,
          mergedRequired.Contains(property.Key),
          schemaName);
        isFirst = false;
      }
    }
    else if (schema.IsType(JsonSchemaType.Array) && schema.Items != null)
    {
      return;
    }

    sb.AppendLine("}");

    string filePath = Path.Combine(outputDir, $"{className}.cs");
    await File.WriteAllTextAsync(filePath, sb.ToString());
  }

  private void GenerateProperty(
    StringBuilder sb,
    string propertyName,
    OpenApiSchema propertySchema,
    bool isRequired,
    string originalSchemaName)
  {
    string propertyNameClean = _typeMapper.GetPropertyName(propertyName);
    // Get the transformed class name for conflict checking
    string className = _typeMapper.GetClassName(originalSchemaName);

    // Ensure property name doesn't conflict with class name
    if (propertyNameClean == className)
    {
      propertyNameClean = propertyNameClean + "Value";
    }

    // Check for property overrides using original schema name
    PropertyOverride? propertyOverride = _config.PropertyOverrides
      .FirstOrDefault(o => o.Matches(propertyName, originalSchemaName, propertySchema.GetEffectiveType().ToString().ToLowerInvariant(), propertySchema.Format));

    // Use enum type if specified, otherwise use target type or inferred type
    string? enumName = propertyOverride?.Enum ?? propertyOverride?.EnumName;

    string propertyType;
    if (!string.IsNullOrEmpty(enumName))
    {
      // Use enum type and make it nullable if not required
      propertyType = enumName + (isRequired ? "" : "?");
    }
    else
    {
      propertyType = propertyOverride?.TargetType ?? GetPropertyType(propertySchema, isRequired, propertyName);
    }

    string indent = _formatting.GetIndentation();

    if (!string.IsNullOrEmpty(propertySchema.Description))
    {
      sb.AppendLine($"{indent}/// <summary>");
      foreach (string line in propertySchema.Description.Split('\n'))
      {
        sb.AppendLine($"{indent}/// {SanitizeXmlDocumentation(line.Trim())}");
      }

      sb.AppendLine($"{indent}/// </summary>");
    }

    if (_options.GenerateDataAnnotations)
    {
      if (isRequired)
      {
        sb.AppendLine($"{indent}[Required]");
      }

      if (propertySchema.IsType(JsonSchemaType.String))
      {
        if (propertySchema.MinLength.HasValue)
        {
          sb.AppendLine($"{indent}[MinLength({propertySchema.MinLength.Value})]");
        }

        if (propertySchema.MaxLength.HasValue)
        {
          sb.AppendLine($"{indent}[MaxLength({propertySchema.MaxLength.Value})]");
        }

        if (!string.IsNullOrEmpty(propertySchema.Pattern))
        {
          sb.AppendLine($"{indent}[RegularExpression(@\"{propertySchema.Pattern.Replace("\"", "\"\"")}\")]");
        }

        if (propertySchema.Format == "email")
        {
          sb.AppendLine($"{indent}[EmailAddress]");
        }

        if (propertySchema.Format == "uri")
        {
          sb.AppendLine($"{indent}[Url]");
        }
      }
      else if (propertySchema.IsType(JsonSchemaType.Integer) || propertySchema.IsType(JsonSchemaType.Number))
      {
        if (!string.IsNullOrEmpty(propertySchema.Minimum) && !string.IsNullOrEmpty(propertySchema.Maximum))
        {
          sb.AppendLine($"{indent}[Range({propertySchema.Minimum}, {propertySchema.Maximum})]");
        }
      }
    }

    // Add JSON converter attribute if specified
    if (!string.IsNullOrEmpty(propertyOverride?.JsonConverter))
    {
      sb.AppendLine($"{indent}[JsonConverter(typeof({propertyOverride.JsonConverter}))]");
    }

    // Add JsonIgnore attribute for Request models with non-nullable optional fields
    // This prevents sending "field": null when the API expects the field to be omitted
    bool isRequestModel = originalSchemaName.EndsWith("Request", StringComparison.OrdinalIgnoreCase);
    bool isNullableInSpec = propertySchema.IsNullable();
    bool isNullableInCSharp = propertyType.EndsWith("?") ||
                              (!propertyType.Contains("int") && !propertyType.Contains("decimal") &&
                               !propertyType.Contains("double") && !propertyType.Contains("float") &&
                               !propertyType.Contains("bool") && !propertyType.Contains("DateTime") &&
                               !propertyType.Contains("DateOnly") && !propertyType.Contains("TimeOnly") &&
                               !propertyType.Contains("Guid"));

    if (isRequestModel && !isRequired && !isNullableInSpec && isNullableInCSharp)
    {
      sb.AppendLine($"{indent}[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]");
    }

    // For required properties with nullable types, ensure they're always serialized
    // This overrides the global WhenWritingNull setting - APIs expect required fields to be present
    if (isRequestModel && isRequired && propertyType.EndsWith("?"))
    {
      sb.AppendLine($"{indent}[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]");
    }

    if (propertyName != propertyNameClean)
    {
      sb.AppendLine($"{indent}[System.Text.Json.Serialization.JsonPropertyName(\"{propertyName}\")]");
    }

    // Use 'required' keyword only for Request models with required reference types
    // For Response models (likely deserialized), use = null!; to suppress CS8618
    if (!isRequestModel && isRequired && !IsValueType(propertyType) && !propertyType.EndsWith("?"))
    {
      sb.AppendLine($"{indent}public {propertyType} {propertyNameClean} {{ get; set; }} = null!;");
    }
    else if (isRequestModel && isRequired && !IsValueType(propertyType) && !propertyType.EndsWith("?"))
    {
      sb.AppendLine($"{indent}public required {propertyType} {propertyNameClean} {{ get; set; }}");
    }
    else
    {
      sb.AppendLine($"{indent}public {propertyType} {propertyNameClean} {{ get; set; }}");
    }
  }

  private async Task GenerateJsonConvertersAsync(string outputDir)
  {
    foreach (JsonConverterConfig converterConfig in _config.JsonConverters)
    {
      if (!string.IsNullOrEmpty(converterConfig.InlineConverterCode))
      {
        StringBuilder converterContent = new();

        // Check if the converter code already has namespace declaration
        bool hasNamespace = converterConfig.InlineConverterCode.Contains("namespace ");
        bool hasUsings = converterConfig.InlineConverterCode.Contains("using ");

        // Only add usings if not already present
        if (!hasUsings)
        {
          HashSet<string> allUsings = new();
          foreach (string reqUsing in converterConfig.RequiredUsings)
          {
            allUsings.Add(reqUsing);
          }

          foreach (string globalUsing in _config.GlobalUsings)
          {
            allUsings.Add(globalUsing);
          }

          foreach (string reqUsing in allUsings)
          {
            converterContent.AppendLine($"using {reqUsing};");
          }

          converterContent.AppendLine();
        }

        // Only add namespace if not already present
        if (!hasNamespace)
        {
          converterContent.AppendLine($"namespace {_options.Namespace};");
          converterContent.AppendLine();
        }

        // Add the inline converter code with proper indentation
        string[] lines = converterConfig.InlineConverterCode.Split('\n');
        foreach (string line in lines)
        {
          if (string.IsNullOrWhiteSpace(line))
          {
            converterContent.AppendLine();
          }
          else
          {
            // Apply formatting to the converter code
            string indentedLine = line.Replace("  ", _formatting.GetIndentation());
            converterContent.AppendLine(indentedLine);
          }
        }

        string converterPath = Path.Combine(outputDir, $"{converterConfig.ConverterType}.cs");
        await File.WriteAllTextAsync(converterPath, converterContent.ToString());
      }
    }
  }

  /// <summary>
  /// Resolves a schema reference to the actual schema definition
  /// </summary>
  private OpenApiSchema ResolveSchemaReference(OpenApiSchema schema)
  {
    if (!string.IsNullOrEmpty(schema.Id) && _currentDocument?.Components?.Schemas != null)
    {
      string referenceName = schema.Id ?? "";
      if (_currentDocument.Components.Schemas.TryGetValue(referenceName, out var resolvedSchema))
      {
        return (OpenApiSchema)resolvedSchema;
      }
    }

    return schema;
  }

  /// <summary>
  /// Sanitizes text for use in XML documentation comments
  /// Removes markdown formatting and encodes XML entities properly
  /// Handles multi-line descriptions with markdown bullet points
  /// </summary>
  private string SanitizeXmlDocumentation(string text)
  {
    if (string.IsNullOrEmpty(text))
    {
      return text;
    }

    // Split by newlines to process each line individually
    string[] lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    List<string> sanitizedLines = new();

    foreach (string line in lines)
    {
      string sanitized = line.Trim();

      // Remove markdown asterisks and dashes at start (bullet points)
      sanitized = sanitized.TrimStart('*', '-', ' ').Trim();

      // Remove markdown backticks
      sanitized = sanitized.Replace("`", "");

      // Apply XML encoding
      sanitized = System.Security.SecurityElement.Escape(sanitized) ?? sanitized;

      if (!string.IsNullOrEmpty(sanitized))
      {
        sanitizedLines.Add(sanitized);
      }
    }

    // Rejoin with newlines
    return string.Join("\n", sanitizedLines);
  }

  private string GetPropertyType(OpenApiSchema schema, bool isRequired, string originalPropertyName = "")
  {
    // Check if the property name contains [] indicating it's an array
    bool isArrayProperty = originalPropertyName.Contains("[]");

    // Handle allOf - if there's a single $ref in allOf, resolve it
    if (schema.AllOf != null && schema.AllOf.Count == 1)
    {
      OpenApiSchema resolvedSchema = ResolveSchemaReference((OpenApiSchema)schema.AllOf[0]);
      return GetPropertyType(resolvedSchema, isRequired, originalPropertyName);
    }

    if (!string.IsNullOrEmpty(schema.Id))
    {
      string referenceName = schema.Id ?? "object";
      string refType = _typeMapper.GetClassName(referenceName) +
                       (_options.GenerateNullableReferenceTypes && !isRequired ? "?" : "");

      if (isArrayProperty)
      {
        return $"{_typeMapper.GetClassName(referenceName)}[]?";
      }

      return refType;
    }

    // Handle array properties with [] in name - take precedence over OpenAPI array type
    if (isArrayProperty && schema.IsType(JsonSchemaType.Array) && schema.Items != null)
    {
      string itemType = GetPropertyType((OpenApiSchema)schema.Items, true);
      // Remove List<> wrapper and any nullable modifiers, then make it a C# array
      if (itemType.StartsWith("List<") && itemType.EndsWith(">?"))
      {
        // Extract inner type from List<Type>?
        string innerType = itemType.Substring(5, itemType.Length - 7);
        return $"{innerType}[]?";
      }
      else if (itemType.EndsWith("?"))
      {
        string cleanType = itemType.Substring(0, itemType.Length - 1);
        return $"{cleanType}[]?";
      }
      else
      {
        return $"{itemType}[]?";
      }
    }

    if (schema.IsType(JsonSchemaType.Array) && schema.Items != null)
    {
      string itemType = GetPropertyType((OpenApiSchema)schema.Items, true);
      return $"List<{itemType}>?";
    }

    if (schema.IsType(JsonSchemaType.Object) && schema.AdditionalProperties != null)
    {
      string valueType = GetPropertyType((OpenApiSchema)schema.AdditionalProperties, true);
      return $"Dictionary<string, {valueType}>?";
    }

    string baseType = _typeMapper.MapOpenApiTypeToClr(schema, _options.GenerateNullableReferenceTypes);

    // If property name contains [], make it an array type
    if (isArrayProperty)
    {
      // Remove the nullable modifier from base type to avoid double nullable
      string cleanBaseType = baseType.EndsWith("?") ? baseType.Substring(0, baseType.Length - 1) : baseType;
      return $"{cleanBaseType}[]?";
    }

    if (_options.GenerateNullableReferenceTypes && !isRequired && !baseType.EndsWith("?"))
    {
      if (baseType == "string" || baseType.EndsWith("[]") || baseType.StartsWith("List<") || baseType.StartsWith("Dictionary<"))
      {
        return baseType + "?";
      }
    }

    // Make optional value types nullable if configured
    if (!isRequired && _config.Serialization.NullableForOptionalProperties && !baseType.EndsWith("?"))
    {
      if (IsValueType(baseType))
      {
        return baseType + "?";
      }
    }

    return baseType;
  }

  private async Task GenerateEnumAsync(string enumName, OpenApiSchema schema, string outputDir)
  {
    string className = _typeMapper.GetClassName(enumName);
    StringBuilder sb = new();

    sb.AppendLine("using System;");
    if (_config.EnumGeneration.AddJsonStringEnumConverter)
    {
      sb.AppendLine("using System.Text.Json.Serialization;");
    }
    sb.AppendLine();
    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    if (!string.IsNullOrEmpty(schema.Description))
    {
      sb.AppendLine("/// <summary>");
      foreach (string line in schema.Description.Split('\n'))
      {
        sb.AppendLine($"/// {SanitizeXmlDocumentation(line.Trim())}");
      }

      sb.AppendLine("/// </summary>");
    }

    if (_config.EnumGeneration.AddJsonStringEnumConverter)
    {
      sb.AppendLine("[JsonConverter(typeof(JsonStringEnumConverter))]");
    }

    sb.AppendLine($"public enum {className}");
    sb.AppendLine("{");

    // Check if this is an integer enum with x-enum-varnames
    bool isIntegerEnum = schema.IsType(JsonSchemaType.Integer);
    List<string>? enumVarNames = null;

    if (isIntegerEnum && schema.Extensions.TryGetValue("x-enum-varnames", out IOpenApiExtension? varNamesExt))
    {
      if (varNamesExt is JsonNodeExtension jne && jne.Node is JsonArray varNamesArray)
      {
        enumVarNames = varNamesArray.Select(v => v?.GetValue<string>() ?? "").ToList();
      }
    }

    for (int i = 0; i < schema.Enum.Count; i++)
    {
      JsonNode? enumValue = schema.Enum[i];
      string enumValueName;
      string? originalStringValue = null;
      string? explicitValue = null;

      if (isIntegerEnum)
      {
        // For integer enums, use x-enum-varnames if available
        if (enumVarNames != null && i < enumVarNames.Count)
        {
          enumValueName = enumVarNames[i];
        }
        else
        {
          enumValueName = enumValue?.ToString() ?? $"Value{i}";
        }

        // Get the integer value
        if (enumValue is JsonValue jsonVal)
        {
          if (jsonVal.TryGetValue<int>(out int intVal))
            explicitValue = intVal.ToString();
          else if (jsonVal.TryGetValue<long>(out long longVal))
            explicitValue = longVal.ToString();
          else
            explicitValue = enumValue?.ToString();
        }
        else
        {
          explicitValue = enumValue?.ToString();
        }
      }
      else
      {
        // For string enums
        if (enumValue is JsonValue jsonStrVal && jsonStrVal.TryGetValue<string>(out string? strVal))
        {
          originalStringValue = strVal ?? "";
          enumValueName = originalStringValue;
        }
        else
        {
          originalStringValue = enumValue?.ToString() ?? "";
          enumValueName = originalStringValue;
        }
      }

      string cleanEnumName = _typeMapper.GetPropertyName(enumValueName);

      // Ensure the enum name is a valid C# identifier
      if (!string.IsNullOrEmpty(cleanEnumName))
      {
        // Replace invalid characters and ensure it starts with a letter or underscore
        cleanEnumName = Regex.Replace(cleanEnumName, @"[^a-zA-Z0-9_]", "_");
        if (char.IsDigit(cleanEnumName[0]))
        {
          cleanEnumName = "_" + cleanEnumName;
        }

        // Add JsonStringEnumMemberName attribute if enabled and we have a string enum
        if (_config.EnumGeneration.AddJsonStringEnumMemberName && !isIntegerEnum && !string.IsNullOrEmpty(originalStringValue))
        {
          sb.AppendLine($"    [JsonStringEnumMemberName(\"{originalStringValue}\")]");
        }

        if (explicitValue != null)
        {
          sb.AppendLine($"    {cleanEnumName} = {explicitValue},");
        }
        else
        {
          sb.AppendLine($"    {cleanEnumName},");
        }
      }
    }

    sb.AppendLine("}");

    string filePath = Path.Combine(outputDir, $"{className}.cs");
    await File.WriteAllTextAsync(filePath, sb.ToString());
  }

  private async Task GenerateRequestModelsAsync(OpenApiDocument document, string outputDir)
  {
    HashSet<string> generatedRequestModels = new();

    foreach (var path in document.Paths)
    {
      foreach (var operation in path.Value.Operations)
      {
        var requestBody = operation.Value.RequestBody;
        if (requestBody?.Content != null)
        {
          var content = requestBody.Content.FirstOrDefault().Value;
          OpenApiSchema? schema = (OpenApiSchema?)content?.Schema;

          // Only process inline schemas (not references)
          // A $ref schema has an Id set even after resolution, so check that
          if (string.IsNullOrEmpty(schema?.Id) && schema?.Properties != null && schema.Properties.Any())
          {
            string? operationId = operation.Value.OperationId;
            if (!string.IsNullOrEmpty(operationId))
            {
              string modelName = operationId.ToDotNetPascalCase() + "Request";

              // Avoid duplicates
              if (!generatedRequestModels.Contains(modelName))
              {
                generatedRequestModels.Add(modelName);
                await GenerateModelClassAsync(modelName, schema, outputDir);
              }
            }
          }
        }
      }
    }
  }


  private async Task GenerateEnumsAsync(string outputDir)
  {
    // Generate legacy/config-defined enums
    foreach (EnumConfig enumConfig in _config.Enums)
    {
      _generatedEnums.Add(enumConfig.Name);
      await GenerateEnumAsync(enumConfig, outputDir);
    }

    // Generate enhanced enums from OpenAPI schemas
    if (_currentDocument?.Components?.Schemas != null)
    {
      EnhancedEnumGenerator enhancedEnumGenerator = new(_config.EnumGeneration, _config.Naming.Overrides);

      foreach (var kvp in _currentDocument.Components.Schemas)
      {
        if (IsEnumSchema((OpenApiSchema)kvp.Value))
        {
          string enumClassName = _typeMapper.GetClassName(kvp.Key);
          _generatedEnums.Add(enumClassName);
          await GenerateEnhancedEnumAsync(kvp.Key, (OpenApiSchema)kvp.Value, enhancedEnumGenerator, outputDir);
        }
      }
    }
  }

  private async Task GenerateEnumAsync(EnumConfig enumConfig, string outputDir)
  {
    StringBuilder sb = new();

    // Add header if enabled
    if (_config.Header.IncludeHeader)
    {
      string header = _config.Header.GenerateHeader(
        _options.InputPath,
        _currentDocument?.Info?.Title,
        _currentDocument?.Info?.Version,
        _config
      );
      sb.Append(header);
    }

    // Collect all using statements and deduplicate
    HashSet<string> allUsings = new()
    {
      "System",
      "System.Text.Json.Serialization",
    };

    // Add global usings from config
    foreach (string globalUsing in _config.GlobalUsings)
    {
      allUsings.Add(globalUsing);
    }

    // Write deduplicated usings in sorted order
    foreach (string usingStatement in allUsings.OrderBy(u => u))
    {
      sb.AppendLine($"using {usingStatement};");
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

    // Generate enum documentation
    sb.AppendLine("/// <summary>");
    sb.AppendLine($"/// {enumConfig.Description ?? enumConfig.Name}");
    sb.AppendLine("/// </summary>");

    // Add JsonConverter attribute if requested
    if (enumConfig.GenerateJsonConverter)
    {
      sb.AppendLine("[JsonConverter(typeof(JsonStringEnumConverter))]");
    }

    sb.AppendLine($"public enum {enumConfig.Name}");
    sb.AppendLine("{");

    // Generate enum values
    List<EnumValue> allValues = enumConfig.GetAllValues().ToList();
    for (int i = 0; i < allValues.Count; i++)
    {
      EnumValue enumValue = allValues[i];

      if (!string.IsNullOrEmpty(enumValue.Description))
      {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// {enumValue.Description}");
        sb.AppendLine("    /// </summary>");
      }

      // Add JsonPropertyName attribute to map to string value
      sb.AppendLine($"    [JsonPropertyName(\"{enumValue.Value}\")]");

      // Add enum member
      if (i == allValues.Count - 1)
      {
        sb.AppendLine($"    {enumValue.Name}");
      }
      else
      {
        sb.AppendLine($"    {enumValue.Name},");
        sb.AppendLine();
      }
    }

    sb.AppendLine("}");

    string fileName = Path.Combine(outputDir, $"{enumConfig.Name}.cs");
    await File.WriteAllTextAsync(fileName, sb.ToString());
  }

  /// <summary>
  /// Checks if a schema only contains null values and no real properties.
  /// These are nullable markers (e.g. {"enum": [null]}) used in oneOf patterns,
  /// not real types that should be generated.
  /// </summary>
  private static bool IsNullOnlySchema(OpenApiSchema schema)
  {
    if (schema.Enum == null || schema.Enum.Count == 0)
      return false;

    bool allNull = schema.Enum.All(e => e == null);
    bool noProperties = schema.Properties == null || schema.Properties.Count == 0;

    return allNull && noProperties;
  }

  /// <summary>
  /// Checks if an OpenAPI schema represents an enum
  /// </summary>
  private bool IsEnumSchema(OpenApiSchema schema)
  {
    if (schema.Enum == null || !schema.Enum.Any())
      return false;

    // Skip enums that only contain null values (e.g. {"enum": [null]})
    bool hasNonNullValues = schema.Enum.Any(e => e != null);
    return hasNonNullValues;
  }

  /// <summary>
  /// Generates an enhanced enum from OpenAPI schema using smart detection
  /// </summary>
  private async Task GenerateEnhancedEnumAsync(
    string enumName,
    OpenApiSchema schema,
    EnhancedEnumGenerator generator,
    string outputDir)
  {
    // Skip if already generated as a legacy enum
    if (_config.Enums.Any(e => e.Name.Equals(enumName, StringComparison.OrdinalIgnoreCase)))
    {
      return;
    }

    // Apply type name overrides to get the final enum name
    string className = _typeMapper.GetClassName(enumName);

    // Analyze the enum with smart detection
    EnhancedEnumInfo enumInfo = generator.AnalyzeEnum(enumName, schema);

    if (!enumInfo.Members.Any())
    {
      return; // Skip empty enums
    }

    StringBuilder sb = new();

    // Add header if enabled
    if (_config.Header.IncludeHeader)
    {
      string header = _config.Header.GenerateHeader(
        _options.InputPath,
        _currentDocument?.Info?.Title,
        _currentDocument?.Info?.Version,
        _config
      );
      sb.Append(header);
    }

    // Collect all using statements
    HashSet<string> allUsings = new()
    {
      "System",
      "System.Text.Json.Serialization",
    };

    if (_config.EnumGeneration.AddDescriptionAttributes)
    {
      allUsings.Add("System.ComponentModel");
    }

    // Always add System.Runtime.Serialization since all enhanced enums use [EnumMember]
    allUsings.Add("System.Runtime.Serialization");

    // Add global usings from config
    foreach (string globalUsing in _config.GlobalUsings)
    {
      allUsings.Add(globalUsing);
    }

    // Write deduplicated usings in sorted order
    foreach (string usingStatement in allUsings.OrderBy(u => u))
    {
      sb.AppendLine($"using {usingStatement};");
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

    // Generate enum documentation
    sb.AppendLine("/// <summary>");
    string description = !string.IsNullOrEmpty(enumInfo.Description)
      ? enumInfo.Description
      : $"Represents {className} values";

    // Split sanitized description by newlines and write each line with /// prefix
    string sanitizedDescription = SanitizeXmlDocumentation(description);
    foreach (string line in sanitizedDescription.Split('\n'))
    {
      sb.AppendLine($"/// {line}");
    }
    sb.AppendLine("/// </summary>");

    // Note: JSON conversion is handled by property-level EmptyStringToNullableEnumConverter

    sb.AppendLine($"public enum {className}");
    sb.AppendLine("{");

    // Add Unknown value if handling unknown values
    if (_config.EnumGeneration.UnknownValueHandling == UnknownValueHandling.UseDefault &&
        !enumInfo.Members.ContainsKey("Unknown"))
    {
      sb.AppendLine("    /// <summary>");
      sb.AppendLine("    /// Unknown or unrecognized value");
      sb.AppendLine("    /// </summary>");
      sb.AppendLine("    [EnumMember(Value = \"unknown\")]");
      if (_config.EnumGeneration.AddDescriptionAttributes)
      {
        sb.AppendLine("    [Description(\"Unknown\")]");
      }

      sb.AppendLine("    Unknown = -1,");
      sb.AppendLine();
    }

    // Generate enum members
    List<EnumMemberInfo> members = enumInfo.Members.Values.OrderBy(m => m.NumericValue ?? int.MaxValue).ToList();
    for (int i = 0; i < members.Count; i++)
    {
      EnumMemberInfo member = members[i];

      // Add documentation
      if (_config.EnumGeneration.AddDescriptionAttributes && !string.IsNullOrEmpty(member.Description))
      {
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// {member.Description}");
        sb.AppendLine("    /// </summary>");
      }

      // Add EnumMember attribute for raw value mapping
      sb.AppendLine($"    [EnumMember(Value = \"{member.RawValue}\")]");

      // Add Description attribute
      if (_config.EnumGeneration.AddDescriptionAttributes && !string.IsNullOrEmpty(member.Description))
      {
        sb.AppendLine($"    [Description(\"{member.Description}\")]");
      }

      // Add enum member with explicit value
      string memberLine = member.NumericValue.HasValue
        ? $"    {member.EnhancedName} = {member.NumericValue.Value}"
        : $"    {member.EnhancedName}";

      if (i == members.Count - 1)
      {
        sb.AppendLine(memberLine);
      }
      else
      {
        sb.AppendLine($"{memberLine},");
        sb.AppendLine();
      }
    }

    sb.AppendLine("}");

    string fileName = Path.Combine(outputDir, $"{className}.cs");
    await File.WriteAllTextAsync(fileName, sb.ToString());

    // Mark as generated to avoid duplicate generation
    _generatedModels.Add(className);
  }

  /// <summary>
  /// Generates enhanced enum support files (converters and extensions)
  /// </summary>
  private async Task GenerateEnhancedEnumSupportAsync(string outputDir)
  {
    // Only generate if enhanced enum generation is enabled
    if (_config.EnumGeneration.GenerationMode == EnumGenerationMode.Raw)
    {
      return;
    }

    await GenerateSmartEnumConverterAsync(outputDir);
    await GenerateEnumExtensionsAsync(outputDir);
  }

  /// <summary>
  /// Generates the SmartEnumConverter class
  /// </summary>
  private async Task GenerateSmartEnumConverterAsync(string outputDir)
  {
    StringBuilder sb = new();

    // Add header if enabled
    if (_config.Header.IncludeHeader)
    {
      string header = _config.Header.GenerateHeader(
        _options.InputPath,
        _currentDocument?.Info?.Title,
        _currentDocument?.Info?.Version,
        _config
      );
      sb.Append(header);
    }

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

    // Copy the SmartEnumConverter implementation
    sb.AppendLine(
      @"/// <summary>
/// Smart JSON converter for enhanced enums that handles both raw values and enum names
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
            _ => _unknownValue
        };
    }

    public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        // Write the raw value (what the API expects)
        if (_enumToString.TryGetValue(value.Value, out var rawValue))
        {
            writer.WriteStringValue(rawValue);
        }
        else
        {
            // Fallback to enum name
            writer.WriteStringValue(value.ToString());
        }
    }

    private TEnum? ParseStringValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        // Try exact match first (handles both raw values and enum names)
        if (_stringToEnum.TryGetValue(value, out var exact))
            return exact;

        // Try numeric string conversion
        if (int.TryParse(value, out var numValue))
        {
            return ParseNumericValue(numValue);
        }

        // Try case-insensitive name match
        if (Enum.TryParse<TEnum>(value, true, out var parsed))
            return parsed;

        return _unknownValue;
    }

    private TEnum? ParseNumberValue(Utf8JsonReader reader)
    {
        if (reader.TryGetInt32(out var intValue))
        {
            return ParseNumericValue(intValue);
        }

        return _unknownValue;
    }

    private TEnum? ParseNumericValue(int value)
    {
        // Try direct cast if defined
        if (Enum.IsDefined(typeof(TEnum), value))
            return (TEnum)(object)value;

        // Try with underscore prefix (_1, _2, etc.)
        var underscoreName = $""_{value}"";
        if (_stringToEnum.TryGetValue(underscoreName, out var prefixed))
            return prefixed;

        return _unknownValue;
    }

    private Dictionary<string, TEnum> BuildStringToEnumMapping()
    {
        var mapping = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);

        foreach (TEnum enumValue in Enum.GetValues<TEnum>())
        {
            var enumName = enumValue.ToString();

            // Add the enum name itself
            mapping[enumName] = enumValue;

            // Add EnumMember value if present
            var memberInfo = typeof(TEnum).GetField(enumName);
            var enumMemberAttr = memberInfo?.GetCustomAttribute<EnumMemberAttribute>();
            if (enumMemberAttr?.Value != null)
            {
                mapping[enumMemberAttr.Value] = enumValue;
            }

            // For numeric enum names like _1, also map to ""1""
            if (enumName.StartsWith(""_"") && int.TryParse(enumName.Substring(1), out var numericValue))
            {
                mapping[numericValue.ToString()] = enumValue;
            }
        }

        return mapping;
    }

    private Dictionary<TEnum, string> BuildEnumToStringMapping()
    {
        var mapping = new Dictionary<TEnum, string>();

        foreach (TEnum enumValue in Enum.GetValues<TEnum>())
        {
            var enumName = enumValue.ToString();

            // Check for EnumMember attribute first (this is the raw API value)
            var memberInfo = typeof(TEnum).GetField(enumName);
            var enumMemberAttr = memberInfo?.GetCustomAttribute<EnumMemberAttribute>();

            if (enumMemberAttr?.Value != null)
            {
                mapping[enumValue] = enumMemberAttr.Value;
            }
            else if (enumName.StartsWith(""_"") && int.TryParse(enumName.Substring(1), out var numericValue))
            {
                // For _1 style enums, use the numeric value
                mapping[enumValue] = numericValue.ToString();
            }
            else
            {
                // Use the enum name as-is
                mapping[enumValue] = enumName;
            }
        }

        return mapping;
    }

    private TEnum? GetUnknownValue()
    {
        // Look for a member named ""Unknown""
        if (Enum.TryParse<TEnum>(""Unknown"", true, out var unknown))
            return unknown;

        // Look for a member with value -1 (common unknown pattern)
        if (Enum.IsDefined(typeof(TEnum), -1))
            return (TEnum)(object)(-1);

        // Return the first enum value as default
        var values = Enum.GetValues<TEnum>();
        return values.Length > 0 ? values[0] : null;
    }
}

/// <summary>
/// Non-nullable version of the smart enum converter
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
/// Factory for creating smart enum converters
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
            // Non-nullable enum
            return (JsonConverter) Activator.CreateInstance(
                typeof(SmartEnumConverterNonNullable<>).MakeGenericType(typeToConvert))!;
        }
        else
        {
            // Nullable enum
            Type enumType = typeToConvert.GetGenericArguments()[0];
            return (JsonConverter) Activator.CreateInstance(
                typeof(SmartEnumConverter<>).MakeGenericType(enumType))!;
        }
    }
}");

    string fileName = Path.Combine(outputDir, "SmartEnumConverter.cs");
    await File.WriteAllTextAsync(fileName, sb.ToString());
  }

  /// <summary>
  /// Generates the EnumExtensions class
  /// </summary>
  private async Task GenerateEnumExtensionsAsync(string outputDir)
  {
    StringBuilder sb = new();

    // Add header if enabled
    if (_config.Header.IncludeHeader)
    {
      string header = _config.Header.GenerateHeader(
        _options.InputPath,
        _currentDocument?.Info?.Title,
        _currentDocument?.Info?.Version,
        _config
      );
      sb.Append(header);
    }

    sb.AppendLine("using System.ComponentModel;");
    sb.AppendLine("using System.Reflection;");
    sb.AppendLine("using System.Runtime.Serialization;");
    sb.AppendLine();

    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    // Copy the EnumExtensions implementation
    sb.AppendLine(
      @"/// <summary>
/// Extension methods for enhanced enum functionality
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// Gets the description of an enum value from the Description attribute
    /// </summary>
    public static string GetDescription<TEnum>(this TEnum enumValue) where TEnum : struct, Enum
    {
        var field = typeof(TEnum).GetField(enumValue.ToString());
        if (field == null) return enumValue.ToString();

        var descriptionAttr = field.GetCustomAttribute<DescriptionAttribute>();
        return descriptionAttr?.Description ?? enumValue.ToString();
    }

    /// <summary>
    /// Gets the raw API value for an enum (from EnumMember attribute or numeric conversion)
    /// </summary>
    public static string GetRawValue<TEnum>(this TEnum enumValue) where TEnum : struct, Enum
    {
        var field = typeof(TEnum).GetField(enumValue.ToString());
        if (field == null) return enumValue.ToString();

        var enumMemberAttr = field.GetCustomAttribute<EnumMemberAttribute>();
        if (enumMemberAttr?.Value != null)
        {
            return enumMemberAttr.Value;
        }

        // For _1 style enums, return the numeric part
        var enumName = enumValue.ToString();
        if (enumName.StartsWith(""_"") && int.TryParse(enumName.Substring(1), out var numericValue))
        {
            return numericValue.ToString();
        }

        return enumName;
    }

    /// <summary>
    /// Checks if an enum value represents an unknown/undefined value
    /// </summary>
    public static bool IsUnknown<TEnum>(this TEnum enumValue) where TEnum : struct, Enum
    {
        var name = enumValue.ToString();
        return name.Equals(""Unknown"", StringComparison.OrdinalIgnoreCase) ||
               Convert.ToInt32(enumValue) == -1;
    }

    /// <summary>
    /// Gets all enum values with their descriptions
    /// </summary>
    public static Dictionary<TEnum, string> GetAllDescriptions<TEnum>() where TEnum : struct, Enum
    {
        var result = new Dictionary<TEnum, string>();

        foreach (TEnum enumValue in Enum.GetValues<TEnum>())
        {
            result[enumValue] = enumValue.GetDescription();
        }

        return result;
    }

    /// <summary>
    /// Tries to parse a raw API value to an enum
    /// </summary>
    public static bool TryParseRawValue<TEnum>(string rawValue, out TEnum result) where TEnum : struct, Enum
    {
        result = default;

        if (string.IsNullOrEmpty(rawValue))
            return false;

        // Try direct enum parse first
        if (Enum.TryParse<TEnum>(rawValue, true, out result))
            return true;

        // Try numeric conversion
        if (int.TryParse(rawValue, out var numericValue))
        {
            // Try with underscore prefix
            if (Enum.TryParse<TEnum>($""_{numericValue}"", true, out result))
                return true;

            // Try direct cast
            if (Enum.IsDefined(typeof(TEnum), numericValue))
            {
                result = (TEnum)(object)numericValue;
                return true;
            }
        }

        // Try finding by EnumMember attribute
        foreach (TEnum enumValue in Enum.GetValues<TEnum>())
        {
            if (enumValue.GetRawValue().Equals(rawValue, StringComparison.OrdinalIgnoreCase))
            {
                result = enumValue;
                return true;
            }
        }

        return false;
    }
}");

    string fileName = Path.Combine(outputDir, "EnumExtensions.cs");
    await File.WriteAllTextAsync(fileName, sb.ToString());
  }

  private async Task GenerateRequestModelAsync(
    string schemaName,
    OpenApiSchema schema,
    string outputDir,
    SchemaVariantGenerator? variantGenerator)
  {
    string requestModelName = $"{schemaName}{_options.ModelGeneration.RequestSuffix}";

    if (_generatedModels.Contains(requestModelName))
    {
      return;
    }

    _generatedModels.Add(requestModelName);

    // Get the request variant
    SchemaVariant? requestVariant = variantGenerator?.GetVariants(schemaName)?[SchemaVariantType.Request];

    // Generate model using filtered properties (no readOnly)
    await GenerateModelFromVariantAsync(requestModelName, schema, requestVariant, outputDir, SchemaVariantType.Request);
  }

  private async Task GenerateResponseModelAsync(
    string schemaName,
    OpenApiSchema schema,
    string outputDir,
    SchemaVariantGenerator? variantGenerator)
  {
    string responseModelName = schemaName + _options.ModelGeneration.ResponseSuffix;

    if (_generatedModels.Contains(responseModelName))
    {
      return;
    }

    _generatedModels.Add(responseModelName);

    // Get the response variant
    SchemaVariant? responseVariant = variantGenerator?.GetVariants(schemaName)?[SchemaVariantType.Response];

    // Generate model using filtered properties (no writeOnly)
    await GenerateModelFromVariantAsync(responseModelName, schema, responseVariant, outputDir, SchemaVariantType.Response);
  }

  /// <summary>
  /// Generates a method-specific model (Create/Update/Patch) with appropriate nullable handling
  /// </summary>
  private async Task GenerateMethodSpecificModelAsync(
    string modelName,
    string schemaName,
    OpenApiSchema schema,
    string outputDir,
    SchemaVariantGenerator? variantGenerator,
    ModelPurpose purpose)
  {
    if (_generatedModels.Contains(modelName))
    {
      return;
    }

    _generatedModels.Add(modelName);

    // Get the request variant (these are all request models)
    SchemaVariant? requestVariant = variantGenerator?.GetVariants(schemaName)?[SchemaVariantType.Request];

    StringBuilder sb = new();

    // Add header
    if (_config.Header.IncludeHeader)
    {
      string header = _config.Header.GenerateHeader(
        _options.InputPath,
        _currentDocument?.Info?.Title,
        _currentDocument?.Info?.Version,
        _config
      );
      sb.Append(header);
    }

    // Collect usings
    HashSet<string> allUsings = new()
    {
      "System",
      "System.Collections.Generic",
    };

    if (_options.GenerateDataAnnotations)
    {
      allUsings.Add("System.ComponentModel.DataAnnotations");
    }

    // Add System.Text.Json.Serialization for JsonIgnore on PATCH models
    if (purpose == ModelPurpose.PatchRequest)
    {
      allUsings.Add("System.Text.Json.Serialization");
    }

    foreach (string globalUsing in _config.GlobalUsings)
    {
      allUsings.Add(globalUsing);
    }

    foreach (PropertyOverride propertyOverride in _config.PropertyOverrides)
    {
      foreach (string reqUsing in propertyOverride.RequiredUsings)
      {
        allUsings.Add(reqUsing);
      }
    }

    foreach (JsonConverterConfig converterConfig in _config.JsonConverters)
    {
      foreach (string reqUsing in converterConfig.RequiredUsings)
      {
        allUsings.Add(reqUsing);
      }
    }

    foreach (string usingStatement in allUsings.OrderBy(u => u))
    {
      sb.AppendLine($"using {usingStatement};");
    }

    sb.AppendLine();

    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    // Add XML documentation
    sb.AppendLine("/// <summary>");
    string purposeDescription = purpose switch
    {
      ModelPurpose.CreateRequest => "Request model for POST operations",
      ModelPurpose.UpdateRequest => "Request model for PUT operations",
      ModelPurpose.PatchRequest => "Request model for PATCH operations (partial updates)",
      _ => "Request model"
    };
    sb.AppendLine($"/// {purposeDescription} for {schemaName}");
    if (!string.IsNullOrEmpty(schema.Description))
    {
      sb.AppendLine("/// ");
      foreach (string line in schema.Description.Split('\n'))
      {
        sb.AppendLine($"/// {SanitizeXmlDocumentation(line.Trim())}");
      }
    }
    sb.AppendLine("/// </summary>");

    string className = _typeMapper.GetClassName(modelName);
    sb.AppendLine($"public class {className}");
    sb.AppendLine("{");

    // Generate properties using variant's filtered property list (no readOnly for requests)
    if (requestVariant != null)
    {
      bool isFirst = true;
      foreach (var property in requestVariant.Properties)
      {
        if (!isFirst)
        {
          sb.AppendLine();
        }
        GeneratePropertyWithPurpose(sb, property.Key, property.Value, requestVariant.Required.Contains(property.Key), purpose, modelName);
        isFirst = false;
      }
    }
    else
    {
      // Fallback: generate all properties except readOnly
      if (schema.Properties != null)
      {
        bool isFirst = true;
        foreach (var property in schema.Properties)
        {
          // Skip readOnly for Request models
          if (property.Value.ReadOnly)
          {
            continue;
          }

          if (!isFirst)
          {
            sb.AppendLine();
          }

          bool isRequired = schema.Required?.Contains(property.Key) ?? false;
          GeneratePropertyWithPurpose(sb, property.Key, (OpenApiSchema)property.Value, isRequired, purpose, modelName);
          isFirst = false;
        }
      }
    }

    sb.AppendLine("}");

    string fileName = Path.Combine(outputDir, $"{className}.cs");
    await File.WriteAllTextAsync(fileName, sb.ToString());
  }

  /// <summary>
  /// Generates a property with nullable handling based on ModelPurpose
  /// </summary>
  private void GeneratePropertyWithPurpose(
    StringBuilder sb,
    string propertyName,
    OpenApiSchema propertySchema,
    bool isRequired,
    ModelPurpose purpose,
    string modelName)
  {
    string propertyNameClean = _typeMapper.GetPropertyName(propertyName);
    string indent = _formatting.GetIndentation();

    // Check for property overrides
    PropertyOverride? propertyOverride = _config.PropertyOverrides
      .FirstOrDefault(o => o.Matches(propertyName, modelName, propertySchema.GetEffectiveType().ToString().ToLowerInvariant(), propertySchema.Format));

    // Use enum type if specified, otherwise use target type or inferred type
    string? enumName = propertyOverride?.Enum ?? propertyOverride?.EnumName;

    string propertyType;
    if (!string.IsNullOrEmpty(enumName))
    {
      // For PATCH models, always make nullable regardless of required
      // For other models, respect isRequired
      bool shouldBeNullable = purpose == ModelPurpose.PatchRequest || !isRequired;
      propertyType = enumName + (shouldBeNullable ? "?" : "");
    }
    else
    {
      // Get base type without considering required (we'll handle that separately)
      propertyType = propertyOverride?.TargetType ?? GetPropertyTypeForPurpose(propertySchema, isRequired, propertyName, purpose);
    }

    // Add XML documentation
    if (!string.IsNullOrEmpty(propertySchema.Description))
    {
      sb.AppendLine($"{indent}/// <summary>");
      foreach (string line in propertySchema.Description.Split('\n'))
      {
        sb.AppendLine($"{indent}/// {SanitizeXmlDocumentation(line.Trim())}");
      }
      sb.AppendLine($"{indent}/// </summary>");
    }

    // Add JSON converter attribute if specified
    if (!string.IsNullOrEmpty(propertyOverride?.JsonConverter))
    {
      sb.AppendLine($"{indent}[JsonConverter(typeof({propertyOverride.JsonConverter}))]");
    }

    // For PATCH models, add JsonIgnore(WhenWritingNull) to all properties
    if (purpose == ModelPurpose.PatchRequest)
    {
      sb.AppendLine($"{indent}[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]");
    }

    // Add JSON property name attribute if different
    if (propertyName != propertyNameClean)
    {
      sb.AppendLine($"{indent}[System.Text.Json.Serialization.JsonPropertyName(\"{propertyName}\")]");
    }

    // Add validation attributes if enabled (but not for PATCH models)
    if (_options.GenerateDataAnnotations && purpose != ModelPurpose.PatchRequest && isRequired)
    {
      sb.AppendLine($"{indent}[Required]");
    }

    // Generate property declaration based on purpose
    string propertyDeclaration = GetPropertyDeclarationForPurpose(propertyType, isRequired, purpose);

    // For Response models, add null-forgiving operator to suppress CS8618
    // Response models are deserialized, not manually constructed, so this is semantically correct
    if (purpose == ModelPurpose.Response && isRequired && !IsValueType(propertyType) && !propertyType.EndsWith("?"))
    {
      sb.AppendLine($"{indent}public {propertyDeclaration} {propertyNameClean} {{ get; set; }} = null!;");
    }
    else
    {
      sb.AppendLine($"{indent}public {propertyDeclaration} {propertyNameClean} {{ get; set; }}");
    }
  }

  /// <summary>
  /// Gets property type with nullable handling based on ModelPurpose
  /// </summary>
  private string GetPropertyTypeForPurpose(OpenApiSchema schema, bool isRequired, string originalPropertyName, ModelPurpose purpose)
  {
    // For PATCH models, we'll always make properties nullable in GetPropertyDeclarationForPurpose
    // So here we just get the base type
    bool treatAsRequired = purpose == ModelPurpose.PatchRequest ? false : isRequired;
    return GetPropertyType(schema, treatAsRequired, originalPropertyName);
  }

  /// <summary>
  /// Gets property declaration with appropriate nullable handling based on ModelPurpose
  /// </summary>
  private string GetPropertyDeclarationForPurpose(string propertyType, bool isRequired, ModelPurpose purpose)
  {
    return purpose switch
    {
      // CreateRequest and UpdateRequest: Use 'required' for required reference types (user-constructed)
      ModelPurpose.CreateRequest or ModelPurpose.UpdateRequest =>
        GetPropertyDeclarationWithRequired(propertyType, isRequired),

      // PatchRequest: Always make nullable (all fields optional for partial updates)
      ModelPurpose.PatchRequest =>
        MakePropertyNullable(propertyType),

      // Response: NO 'required' keyword - these are deserialized, not manually constructed
      ModelPurpose.Response =>
        propertyType,

      _ => propertyType
    };
  }

  /// <summary>
  /// Gets property declaration with 'required' keyword for required reference types
  /// </summary>
  private string GetPropertyDeclarationWithRequired(string propertyType, bool isRequired)
  {
    if (!isRequired)
    {
      return propertyType;
    }

    // Already nullable - no need for required
    if (propertyType.EndsWith("?"))
    {
      return propertyType;
    }

    // Check if it's a value type (these don't need 'required')
    bool isValueType = IsValueType(propertyType);

    // Use 'required' keyword for required reference types (.NET 7+)
    if (_options.GenerateNullableReferenceTypes && !isValueType)
    {
      return $"required {propertyType}";
    }

    return propertyType;
  }

  /// <summary>
  /// Checks if a type is a value type (doesn't need 'required' keyword)
  /// </summary>
  private bool IsValueType(string propertyType)
  {
    // Arrays are reference types
    if (propertyType.Contains("[]") || propertyType.Contains("<"))
    {
      return false;
    }

    // Strip nullable suffix for comparison
    string baseType = propertyType.TrimEnd('?');

    // Enums are value types
    if (_generatedEnums.Contains(baseType))
    {
      return true;
    }

    // Common value types
    string[] valueTypes = { "int", "long", "decimal", "double", "float", "bool", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "Guid", "byte", "short", "uint", "ulong", "ushort", "sbyte", "char" };
    return valueTypes.Any(vt => propertyType == vt || propertyType.StartsWith($"{vt}?"));
  }

  /// <summary>
  /// Makes a property type nullable (for PATCH models)
  /// </summary>
  private string MakePropertyNullable(string propertyType)
  {
    // If already nullable, return as-is
    if (propertyType.EndsWith("?"))
    {
      return propertyType;
    }

    // Add nullable modifier
    return $"{propertyType}?";
  }

  private async Task GenerateModelFromVariantAsync(
    string modelName,
    OpenApiSchema schema,
    SchemaVariant? variant,
    string outputDir,
    SchemaVariantType variantType)
  {
    StringBuilder sb = new();

    // Add header
    if (_config.Header.IncludeHeader)
    {
      string header = _config.Header.GenerateHeader(
        _options.InputPath,
        _currentDocument?.Info?.Title,
        _currentDocument?.Info?.Version,
        _config
      );
      sb.Append(header);
    }

    // Collect usings
    HashSet<string> allUsings = new()
    {
      "System",
      "System.Collections.Generic",
    };

    if (_options.GenerateDataAnnotations)
    {
      allUsings.Add("System.ComponentModel.DataAnnotations");
    }

    foreach (string globalUsing in _config.GlobalUsings)
    {
      allUsings.Add(globalUsing);
    }

    foreach (PropertyOverride propertyOverride in _config.PropertyOverrides)
    {
      foreach (string reqUsing in propertyOverride.RequiredUsings)
      {
        allUsings.Add(reqUsing);
      }
    }

    foreach (JsonConverterConfig converterConfig in _config.JsonConverters)
    {
      foreach (string reqUsing in converterConfig.RequiredUsings)
      {
        allUsings.Add(reqUsing);
      }
    }

    foreach (string usingStatement in allUsings.OrderBy(u => u))
    {
      sb.AppendLine($"using {usingStatement};");
    }

    sb.AppendLine();

    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    // Add XML documentation
    sb.AppendLine("/// <summary>");
    if (!string.IsNullOrEmpty(schema.Description))
    {
      sb.AppendLine($"/// {schema.Description}");
    }
    else
    {
      string variantDescription = variantType == SchemaVariantType.Request ? "Request model" : "Response model";
      string baseName = modelName;
      if (!string.IsNullOrEmpty(_options.ModelGeneration.RequestSuffix))
      {
        baseName = baseName.Replace(_options.ModelGeneration.RequestSuffix, "");
      }
      if (!string.IsNullOrEmpty(_options.ModelGeneration.ResponseSuffix))
      {
        baseName = baseName.Replace(_options.ModelGeneration.ResponseSuffix, "");
      }
      sb.AppendLine($"/// {variantDescription} for {baseName}");
    }
    sb.AppendLine("/// </summary>");

    string className = _typeMapper.GetClassName(modelName);
    sb.AppendLine($"public class {className}");
    sb.AppendLine("{");

    // Generate properties using variant's filtered property list
    if (variant != null)
    {
      foreach (var property in variant.Properties)
      {
        GeneratePropertyForVariant(sb, property.Key, property.Value, variant.Required.Contains(property.Key), variantType, modelName);
      }
    }
    else
    {
      // Fallback: generate all properties
      if (schema.Properties != null)
      {
        foreach (var property in schema.Properties)
        {
          bool isReadOnly = property.Value.ReadOnly;
          bool isWriteOnly = property.Value.WriteOnly;

          // Skip readOnly for Request, skip writeOnly for Response
          if ((variantType == SchemaVariantType.Request && isReadOnly) ||
              (variantType == SchemaVariantType.Response && isWriteOnly))
          {
            continue;
          }

          bool isRequired = schema.Required?.Contains(property.Key) ?? false;
          GeneratePropertyForVariant(sb, property.Key, (OpenApiSchema)property.Value, isRequired, variantType, modelName);
        }
      }
    }

    sb.AppendLine("}");

    string fileName = Path.Combine(outputDir, $"{className}.cs");
    await File.WriteAllTextAsync(fileName, sb.ToString());
  }

  private void GeneratePropertyForVariant(
    StringBuilder sb,
    string propertyName,
    OpenApiSchema propertySchema,
    bool isRequired,
    SchemaVariantType variantType,
    string modelName)
  {
    // Use existing GenerateProperty logic but respect variant type
    string propertyNamePascal = propertyName.ToDotNetPascalCase();

    // Add XML documentation
    if (!string.IsNullOrEmpty(propertySchema.Description))
    {
      sb.AppendLine($"  /// <summary>");
      sb.AppendLine($"  /// {SanitizeXmlDocumentation(propertySchema.Description)}");
      sb.AppendLine($"  /// </summary>");
    }

    // Check for property overrides FIRST using original schema information
    PropertyOverride? propertyOverride = _config.PropertyOverrides
      .FirstOrDefault(o => o.Matches(propertyName, modelName, propertySchema.GetEffectiveType().ToString().ToLowerInvariant(), propertySchema.Format));

    // Use enum type if specified, otherwise use target type or inferred type
    string? enumName = propertyOverride?.Enum ?? propertyOverride?.EnumName;

    string propertyType;
    if (!string.IsNullOrEmpty(enumName))
    {
      // Use enum type and make it nullable if not required
      propertyType = enumName + (isRequired ? "" : "?");
    }
    else
    {
      propertyType = propertyOverride?.TargetType ?? GetPropertyType(propertySchema, isRequired, propertyName);
    }

    // Add JSON converter attribute if specified
    if (!string.IsNullOrEmpty(propertyOverride?.JsonConverter))
    {
      sb.AppendLine($"  [JsonConverter(typeof({propertyOverride.JsonConverter}))]");
    }

    // Add JsonIgnore attribute for Request models with non-nullable optional fields
    bool isRequestModel = variantType == SchemaVariantType.Request;
    bool isNullableInSpec = propertySchema.IsNullable();
    string nullSuffix = ShouldBeNullable(propertyType, isRequired) ? "?" : "";
    bool isNullableInCSharp = nullSuffix.Contains("?") ||
                              (!propertyType.Contains("int") && !propertyType.Contains("decimal") &&
                               !propertyType.Contains("double") && !propertyType.Contains("float") &&
                               !propertyType.Contains("bool") && !propertyType.Contains("DateTime") &&
                               !propertyType.Contains("DateOnly") && !propertyType.Contains("TimeOnly") &&
                               !propertyType.Contains("Guid"));

    if (isRequestModel && !isRequired && !isNullableInSpec && isNullableInCSharp)
    {
      sb.AppendLine("  [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]");
    }

    // Add JSON property attribute
    sb.AppendLine($"  [JsonPropertyName(\"{propertyName}\")]");

    // Add validation attributes if enabled
    if (_options.GenerateDataAnnotations && isRequired)
    {
      sb.AppendLine("  [Required]");
    }

    // Generate property with appropriate handling for Response vs Request models
    bool isResponseModel = variantType == SchemaVariantType.Response;

    // For Response models: NO 'required' keyword, use = null!; for required reference types
    // For Request models: Use 'required' keyword for required reference types
    if (isResponseModel && isRequired && !IsValueType(propertyType) && !propertyType.EndsWith("?") && !nullSuffix.Contains("?"))
    {
      sb.AppendLine($"  public {propertyType}{nullSuffix} {propertyNamePascal} {{ get; set; }} = null!;");
    }
    else if (!isResponseModel && isRequired && !IsValueType(propertyType) && !propertyType.EndsWith("?") && !nullSuffix.Contains("?"))
    {
      // Request models use 'required' keyword
      sb.AppendLine($"  public required {propertyType}{nullSuffix} {propertyNamePascal} {{ get; set; }}");
    }
    else
    {
      sb.AppendLine($"  public {propertyType}{nullSuffix} {propertyNamePascal} {{ get; set; }}");
    }
    sb.AppendLine();
  }

  private bool ShouldBeNullable(string propertyType, bool isRequired)
  {
    // Value types that aren't already nullable should be made nullable if not required
    if (!isRequired && !propertyType.EndsWith("?"))
    {
      string[] valueTypes = { "int", "long", "decimal", "double", "float", "bool", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "Guid" };
      return valueTypes.Any(vt => propertyType == vt || propertyType.StartsWith($"{vt}<"));
    }

    return false;
  }

  private async Task GenerateToRequestExtensionsAsync(
    string outputDir,
    Dictionary<string, ModelGenerationDecision> decisions,
    OpenApiDocument document)
  {
    StringBuilder sb = new();

    // Add header
    if (_config.Header.IncludeHeader)
    {
      string header = _config.Header.GenerateHeader(
        _options.InputPath,
        document?.Info?.Title,
        document?.Info?.Version,
        _config
      );
      sb.Append(header);
    }

    sb.AppendLine("using System;");
    sb.AppendLine();

    if (_options.GenerateNullableReferenceTypes)
    {
      sb.AppendLine("#nullable enable");
      sb.AppendLine();
    }

    sb.AppendLine($"namespace {_options.Namespace};");
    sb.AppendLine();

    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Extension methods for converting between Request and Response models");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("public static class ModelConversionExtensions");
    sb.AppendLine("{");

    // Generate ToRequest() for each split schema
    foreach (ModelGenerationDecision decision in decisions.Values.Where(d => d.ShouldSplit))
    {
      // Apply type name overrides
      TypeNameOverride? typeOverride = _config.TypeNameOverrides?.FirstOrDefault(o => o.Matches(decision.SchemaName));
      string effectiveSchemaName = typeOverride?.NewName ?? decision.SchemaName;

      // Don't use decision.ResponseModelName/RequestModelName - they have original names
      // Always construct from the effectiveSchemaName (after type override)
      string responseModelName = effectiveSchemaName;
      string requestModelName = $"{effectiveSchemaName}{_options.ModelGeneration.RequestSuffix}";

      sb.AppendLine($"  /// <summary>");
      sb.AppendLine($"  /// Converts {responseModelName} to {requestModelName}");
      sb.AppendLine($"  /// </summary>");
      sb.AppendLine($"  public static {requestModelName} ToRequest(this {responseModelName} source)");
      sb.AppendLine("  {");
      sb.AppendLine($"    return new {requestModelName}");
      sb.AppendLine("    {");

      // Map properties that exist on BOTH the response and request models.
      // The request model may come from:
      //   a) A separate schema in the spec (e.g., PaymentRequest)
      //   b) A generated variant that filters out readOnly properties
      // We must only map properties that the request model actually has.
      OpenApiComponents? components = document?.Components;
      if (components?.Schemas is { } schemas &&
          schemas.TryGetValue(decision.SchemaName, out var iSchema))
      {
        var schema = (OpenApiSchema)iSchema;
        if (schema.Properties != null)
        {
          // Determine which properties the request model has, with their schemas
          Dictionary<string, OpenApiSchema> requestProperties = new(StringComparer.OrdinalIgnoreCase);

          // Check if there's a separate request schema in the spec
          string requestSchemaName = $"{decision.SchemaName}Request";
          if (schemas.TryGetValue(requestSchemaName, out var iRequestSchema) &&
              iRequestSchema.Properties != null)
          {
            var requestSchema = (OpenApiSchema)iRequestSchema;
            foreach (var kvp in requestSchema.Properties)
              requestProperties[kvp.Key] = (OpenApiSchema)kvp.Value;
          }

          // If no separate request schema, use the response schema minus readOnly/writeOnly
          if (requestProperties.Count == 0)
          {
            foreach (var property in schema.Properties)
            {
              if (!property.Value.ReadOnly && !property.Value.WriteOnly)
                requestProperties[property.Key] = (OpenApiSchema)property.Value;
            }
          }

          List<string> propertyMappings = new();

          foreach (var property in schema.Properties)
          {
            // Only map properties that exist on both models and have compatible final C# types
            if (requestProperties.TryGetValue(property.Key, out OpenApiSchema? requestProp) &&
                !property.Value.WriteOnly)
            {
              // Compare the final C# types (after property overrides) to ensure compatibility
              string sourceType = ResolvePropertyType(property.Key, (OpenApiSchema)property.Value, false, decision.SchemaName);
              string targetType = ResolvePropertyType(property.Key, requestProp, false, requestSchemaName);

              // Check type compatibility for assignment: target = source
              // Exact match always works.
              // T -> T? works (implicit conversion).
              // T? -> T does NOT work (would need .Value).
              string sourceBase = sourceType.TrimEnd('?');
              string targetBase = targetType.TrimEnd('?');
              bool baseTypesMatch = sourceBase == targetBase;
              bool sourceIsNullable = sourceType.EndsWith("?");
              bool targetIsNullable = targetType.EndsWith("?");
              bool isAssignable = baseTypesMatch && (sourceType == targetType || (!sourceIsNullable && targetIsNullable));

              if (isAssignable)
              {
                string propName = property.Key.ToDotNetPascalCase();
                propertyMappings.Add($"      {propName} = source.{propName}");
              }
            }
          }

          sb.AppendLine(string.Join(",\n", propertyMappings));
        }
      }

      sb.AppendLine("    };");
      sb.AppendLine("  }");
      sb.AppendLine();
    }

    sb.AppendLine("}");

    string fileName = Path.Combine(outputDir, "ModelConversionExtensions.cs");
    await File.WriteAllTextAsync(fileName, sb.ToString());
  }

  /// <summary>
  /// Resolves the final C# type for a property, taking into account property overrides from config.
  /// </summary>
  private string ResolvePropertyType(string propertyName, OpenApiSchema propertySchema, bool isRequired, string modelName)
  {
    PropertyOverride? propertyOverride = _config.PropertyOverrides
      .FirstOrDefault(o => o.Matches(propertyName, modelName, propertySchema.GetEffectiveType().ToString().ToLowerInvariant(), propertySchema.Format));

    string? enumName = propertyOverride?.Enum ?? propertyOverride?.EnumName;

    if (!string.IsNullOrEmpty(enumName))
    {
      return enumName + (isRequired ? "" : "?");
    }

    return propertyOverride?.TargetType ?? GetPropertyType(propertySchema, isRequired, propertyName);
  }
}