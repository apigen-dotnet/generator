namespace Apigen.Generator.Models;

/// <summary>
/// Configuration options for model generation strategy
/// </summary>
public class ModelGenerationOptions
{
  /// <summary>
  /// Strategy for generating request/response models
  /// </summary>
  public ModelGenerationStrategy Strategy { get; set; } = ModelGenerationStrategy.SeparateWithDeduplication;

  /// <summary>
  /// Suffix to add to request models (e.g., "Request", "CreateDto")
  /// </summary>
  public string RequestSuffix { get; set; } = "Request";

  /// <summary>
  /// Suffix to add to response models (empty by default)
  /// </summary>
  public string ResponseSuffix { get; set; } = "";

  /// <summary>
  /// Generate .ToRequest() extension methods for converting response models to request models
  /// </summary>
  public bool GenerateToRequestExtensions { get; set; } = true;

  /// <summary>
  /// Generate .ToResponse() extension methods for converting request models to response models
  /// </summary>
  public bool GenerateToResponseExtensions { get; set; } = false;
}

/// <summary>
/// Strategy for generating models
/// </summary>
public enum ModelGenerationStrategy
{
  /// <summary>
  /// Always generate separate Request and Response models, even if identical
  /// </summary>
  AlwaysSeparate,

  /// <summary>
  /// Generate separate models only when they differ, otherwise use single model (default)
  /// </summary>
  SeparateWithDeduplication,

  /// <summary>
  /// Always use single model (legacy behavior)
  /// </summary>
  SingleModel
}
