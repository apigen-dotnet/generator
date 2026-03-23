namespace Apigen.Generator.Models;

public class GeneratorOptions
{
  public string InputPath { get; set; } = string.Empty;
  public string OutputPath { get; set; } = "Generated";
  public string Namespace { get; set; } = "GeneratedApi.Models";
  public string ProjectName { get; set; } = "GeneratedApi.Models";
  public string TargetFramework { get; set; } = "net8.0";
  public bool GenerateNullableReferenceTypes { get; set; } = true;
  public bool GenerateDataAnnotations { get; set; } = true;

  /// <summary>
  /// Model generation strategy configuration
  /// </summary>
  public ModelGenerationOptions ModelGeneration { get; set; } = new();
}