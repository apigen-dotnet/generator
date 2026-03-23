namespace Apigen.Generator.Models;

/// <summary>
/// Configuration options for model generation
/// </summary>
public class ModelsOptions
{
  /// <summary>
  /// Namespace for the generated models
  /// </summary>
  public string Namespace { get; set; } = "GeneratedApi.Models";

  /// <summary>
  /// Project name for the generated models
  /// </summary>
  public string ProjectName { get; set; } = "GeneratedApi.Models";
}
