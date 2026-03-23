using Apigen.Generator.Models;

namespace Apigen.Generator.Services;

/// <summary>
/// Determines whether to generate separate Request/Response models or deduplicate into a single model
/// </summary>
public class ModelDeduplicator
{
  private readonly Dictionary<string, SchemaUsage> _usageMap;
  private readonly Dictionary<string, Dictionary<SchemaVariantType, SchemaVariant>> _variants;
  private readonly Dictionary<string, ModelGenerationDecision> _decisions = new();

  public ModelDeduplicator(
    Dictionary<string, SchemaUsage> usageMap,
    Dictionary<string, Dictionary<SchemaVariantType, SchemaVariant>> variants)
  {
    _usageMap = usageMap;
    _variants = variants;
  }

  /// <summary>
  /// Analyzes all schemas and decides which should be split and which should be unified
  /// </summary>
  public Dictionary<string, ModelGenerationDecision> MakeDecisions()
  {
    _decisions.Clear();

    // Process schemas in dependency order (leaf nodes first)
    List<string> processingOrder = GetDependencyOrder();

    foreach (string schemaName in processingOrder)
    {
      MakeDecisionForSchema(schemaName);
    }

    return _decisions;
  }

  private void MakeDecisionForSchema(string schemaName)
  {
    if (!_variants.TryGetValue(schemaName, out Dictionary<SchemaVariantType, SchemaVariant>? variants))
    {
      return;
    }

    if (!_usageMap.TryGetValue(schemaName, out SchemaUsage? usage))
    {
      return;
    }

    ModelGenerationDecision decision = new ModelGenerationDecision
    {
      SchemaName = schemaName,
      Usage = usage
    };

    // Skip method-specific generation if schema name already indicates it's method-specific
    if (IsAlreadyMethodSpecific(schemaName))
    {
      decision.ShouldSplit = false;
      decision.Reason = "Schema name already indicates method-specific purpose";
      decision.ModelsToGenerate = new[] { schemaName };
      _decisions[schemaName] = decision;
      return;
    }

    // If only used in requests or only in responses, check if we need method-specific models
    if (!usage.IsUsedInBoth)
    {
      if (usage.IsUsedInRequests && ShouldGenerateMethodSpecificModels(usage))
      {
        // Generate method-specific request models even if no response model
        decision.ShouldSplit = false;
        decision.ShouldGenerateMethodSpecificModels = true;
        List<string> modelsToGenerate = new();

        if (usage.IsUsedInPostRequest)
        {
          decision.CreateModelName = $"{schemaName}CreateRequest";
          modelsToGenerate.Add(decision.CreateModelName);
        }

        if (usage.IsUsedInPutRequest)
        {
          decision.UpdateModelName = $"{schemaName}UpdateRequest";
          modelsToGenerate.Add(decision.UpdateModelName);
        }

        if (usage.IsUsedInPatchRequest)
        {
          decision.PatchModelName = $"{schemaName}PatchRequest";
          modelsToGenerate.Add(decision.PatchModelName);
        }

        decision.ModelsToGenerate = modelsToGenerate.ToArray();
        decision.Reason = "Only used in requests - using method-specific models";
        _decisions[schemaName] = decision;
        return;
      }

      decision.ShouldSplit = false;
      decision.Reason = usage.IsUsedInRequests
        ? "Only used in requests"
        : "Only used in responses";
      decision.ModelsToGenerate = new[] { schemaName };
      _decisions[schemaName] = decision;
      return;
    }

    // If schema name already ends with Request/Response, respect existing naming
    if (schemaName.EndsWith("Request", StringComparison.OrdinalIgnoreCase) ||
        schemaName.EndsWith("Response", StringComparison.OrdinalIgnoreCase))
    {
      decision.ShouldSplit = false;
      decision.Reason = "Already has Request/Response suffix";
      decision.ModelsToGenerate = new[] { schemaName };
      _decisions[schemaName] = decision;
      return;
    }

    // Get the variants
    bool hasRequestVariant = variants.TryGetValue(SchemaVariantType.Request, out SchemaVariant? requestVariant);
    bool hasResponseVariant = variants.TryGetValue(SchemaVariantType.Response, out SchemaVariant? responseVariant);

    if (!hasRequestVariant || !hasResponseVariant)
    {
      decision.ShouldSplit = false;
      decision.Reason = "Missing variant";
      decision.ModelsToGenerate = new[] { schemaName };
      _decisions[schemaName] = decision;
      return;
    }

    // Check if any nested schemas were split - if so, this must split too
    bool nestedSchemaWasSplit = requestVariant != null && CheckNestedSchemasSplit(requestVariant);

    if (nestedSchemaWasSplit)
    {
      decision.ShouldSplit = true;
      decision.Reason = "Nested schema was split";
      decision.ModelsToGenerate = new[] { $"{schemaName}Request", schemaName };
      decision.RequestModelName = $"{schemaName}Request";
      decision.ResponseModelName = schemaName;
      _decisions[schemaName] = decision;
      return;
    }

    // Compare structure hashes
    if (requestVariant != null && responseVariant != null &&
        requestVariant.StructureHash == responseVariant.StructureHash)
    {
      decision.ShouldSplit = false;
      decision.Reason = "Request and Response structures are identical";
      decision.ModelsToGenerate = new[] { schemaName };
      _decisions[schemaName] = decision;
      return;
    }

    // Structures differ - need to split
    decision.ShouldSplit = true;
    decision.Reason = "Request and Response structures differ";

    // Check if we should generate method-specific models
    bool shouldGenerateMethodSpecific = ShouldGenerateMethodSpecificModels(usage);

    if (shouldGenerateMethodSpecific)
    {
      decision.ShouldGenerateMethodSpecificModels = true;
      List<string> modelsToGenerate = new();

      // Always generate response model
      modelsToGenerate.Add(schemaName);
      decision.ResponseModelName = schemaName;

      // Generate method-specific request models
      if (usage.IsUsedInPostRequest)
      {
        decision.CreateModelName = $"{schemaName}CreateRequest";
        modelsToGenerate.Add(decision.CreateModelName);
      }

      if (usage.IsUsedInPutRequest)
      {
        decision.UpdateModelName = $"{schemaName}UpdateRequest";
        modelsToGenerate.Add(decision.UpdateModelName);
      }

      if (usage.IsUsedInPatchRequest)
      {
        decision.PatchModelName = $"{schemaName}PatchRequest";
        modelsToGenerate.Add(decision.PatchModelName);
      }

      decision.ModelsToGenerate = modelsToGenerate.ToArray();
      decision.Reason = "Request and Response structures differ - using method-specific models";
    }
    else
    {
      // Standard split: one Request model, one Response model
      decision.ModelsToGenerate = new[] { $"{schemaName}Request", schemaName };
      decision.RequestModelName = $"{schemaName}Request";
      decision.ResponseModelName = schemaName;
    }

    _decisions[schemaName] = decision;
  }

  /// <summary>
  /// Checks if a schema name already indicates it's method-specific
  /// (e.g., "PatchedUserRequest", "CreateUserRequest", "UpdateUserRequest")
  /// </summary>
  private static bool IsAlreadyMethodSpecific(string schemaName)
  {
    if (string.IsNullOrEmpty(schemaName))
      return false;

    string[] prefixes = { "Patched" };
    string[] suffixes = { "CreateRequest", "UpdateRequest", "PatchRequest" };

    foreach (string p in prefixes)
      if (schemaName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
        return true;

    foreach (string s in suffixes)
      if (schemaName.EndsWith(s, StringComparison.OrdinalIgnoreCase))
        return true;

    return false;
  }

  /// <summary>
  /// Determines if method-specific models should be generated for this schema
  /// </summary>
  private bool ShouldGenerateMethodSpecificModels(SchemaUsage usage)
  {
    // Generate method-specific models if PATCH is used
    // This allows different nullable handling for PATCH vs POST/PUT
    return usage.IsUsedInPatchRequest;
  }

  /// <summary>
  /// Checks if any nested schemas were split (have Request/Response variants)
  /// </summary>
  private bool CheckNestedSchemasSplit(SchemaVariant variant)
  {
    foreach (string nestedSchema in variant.NestedReferences.Values)
    {
      if (_decisions.TryGetValue(nestedSchema, out ModelGenerationDecision? nestedDecision))
      {
        if (nestedDecision.ShouldSplit)
        {
          return true;
        }
      }
    }

    return false;
  }

  /// <summary>
  /// Returns schemas in dependency order (leaf nodes first, then parents)
  /// This ensures we process nested schemas before their parents
  /// </summary>
  private List<string> GetDependencyOrder()
  {
    List<string> result = new List<string>();
    HashSet<string> visited = new HashSet<string>();
    HashSet<string> inProgress = new HashSet<string>();

    foreach (string schemaName in _usageMap.Keys)
    {
      Visit(schemaName, visited, inProgress, result);
    }

    return result;
  }

  private void Visit(string schemaName, HashSet<string> visited, HashSet<string> inProgress, List<string> result)
  {
    if (visited.Contains(schemaName))
    {
      return;
    }

    if (inProgress.Contains(schemaName))
    {
      // Circular dependency - just skip
      return;
    }

    inProgress.Add(schemaName);

    // Visit dependencies first (nested schemas)
    if (_usageMap.TryGetValue(schemaName, out SchemaUsage? usage))
    {
      foreach (string referenced in usage.References)
      {
        Visit(referenced, visited, inProgress, result);
      }
    }

    inProgress.Remove(schemaName);
    visited.Add(schemaName);
    result.Add(schemaName);
  }

  /// <summary>
  /// Gets the decision for a specific schema
  /// </summary>
  public ModelGenerationDecision? GetDecision(string schemaName)
  {
    return _decisions.TryGetValue(schemaName, out ModelGenerationDecision? decision) ? decision : null;
  }
}

/// <summary>
/// Represents the decision about how to generate models for a schema
/// </summary>
public class ModelGenerationDecision
{
  public string SchemaName { get; set; } = string.Empty;
  public SchemaUsage Usage { get; set; } = null!;
  public bool ShouldSplit { get; set; }
  public string Reason { get; set; } = string.Empty;
  public string[] ModelsToGenerate { get; set; } = Array.Empty<string>();
  public string? RequestModelName { get; set; }
  public string? ResponseModelName { get; set; }

  // Method-specific model generation
  public bool ShouldGenerateMethodSpecificModels { get; set; }
  public string? CreateModelName { get; set; }  // POST
  public string? UpdateModelName { get; set; }  // PUT
  public string? PatchModelName { get; set; }   // PATCH
}
