namespace Apigen.Generator.Models;

public class JsonConverterConfig
{
  /// <summary>
  /// The C# type that this converter handles
  /// </summary>
  public string TargetType { get; set; } = string.Empty;

  /// <summary>
  /// The JSON converter class name (e.g., "UnixTimestampConverter")
  /// </summary>
  public string ConverterType { get; set; } = string.Empty;

  /// <summary>
  /// Namespace where the converter is located
  /// </summary>
  public string? ConverterNamespace { get; set; }

  /// <summary>
  /// Additional using statements required for the converter
  /// </summary>
  public List<string> RequiredUsings { get; set; } = new();

  /// <summary>
  /// Optional: Custom converter implementation to generate inline
  /// </summary>
  public string? InlineConverterCode { get; set; }

  /// <summary>
  /// Optional: Path to external converter file (relative to config file or absolute)
  /// </summary>
  public string? ConverterFilePath { get; set; }

  /// <summary>
  /// Get the full converter attribute string
  /// </summary>
  public string GetConverterAttribute()
  {
    string converterName = string.IsNullOrEmpty(ConverterNamespace)
      ? ConverterType
      : $"{ConverterNamespace}.{ConverterType}";

    return $"[JsonConverter(typeof({converterName}))]";
  }
}