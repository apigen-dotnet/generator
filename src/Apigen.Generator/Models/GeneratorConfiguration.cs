using System.Text.Json;
using Tomlyn;

namespace Apigen.Generator.Models;

// Tomlyn 2.x uses System.Text.Json-style property naming; snake_case policy matches TOML conventions
// UseILogger needs explicit [JsonPropertyName] because SnakeCaseLower splits it as "use_i_logger" instead of "use_ilogger"

public class GeneratorConfiguration
{
  /// <summary>
  /// OpenAPI spec inputs. Each entry has a path and optional path_prefix.
  /// Replaces the old input_path single-spec field.
  /// </summary>
  public List<SpecConfiguration> Specs { get; set; } = new();
  public string OutputPath { get; set; } = "Generated";
  public string TargetFramework { get; set; } = "net8.0";
  public bool GenerateNullableReferenceTypes { get; set; } = true;
  public bool GenerateDataAnnotations { get; set; } = true;
  public CodeFormattingOptions Formatting { get; set; } = new();

  /// <summary>
  /// Model generation configuration (namespace, project name, etc.)
  /// </summary>
  public ModelsOptions Models { get; set; } = new();

  /// <summary>
  /// Property type overrides - allows customizing specific property types
  /// </summary>
  public List<PropertyOverride> PropertyOverrides { get; set; } = new();

  /// <summary>
  /// Response type overrides - allows customizing operation response types
  /// </summary>
  public List<ResponseTypeOverride> ResponseTypeOverrides { get; set; } = new();

  /// <summary>
  /// JSON converter configurations
  /// </summary>
  public List<JsonConverterConfig> JsonConverters { get; set; } = new();

  /// <summary>
  /// Additional global using statements to include in all generated files
  /// </summary>
  public List<string> GlobalUsings { get; set; } = new();

  /// <summary>
  /// Type name overrides - allows renaming conflicting type names
  /// </summary>
  public List<TypeNameOverride> TypeNameOverrides { get; set; } = new();

  /// <summary>
  /// Header generation options
  /// </summary>
  public HeaderOptions Header { get; set; } = new();

  /// <summary>
  /// API Client generation options
  /// </summary>
  public ClientGenerationOptions Client { get; set; } = new();

  /// <summary>
  /// Naming convention options
  /// </summary>
  public NamingOptions Naming { get; set; } = new();

  /// <summary>
  /// Enum definitions for properties that should use enum types
  /// </summary>
  public List<EnumConfig> Enums { get; set; } = new();

  /// <summary>
  /// Enhanced enum generation configuration
  /// </summary>
  public EnumGenerationOptions EnumGeneration { get; set; } = new();

  /// <summary>
  /// Model generation strategy configuration
  /// </summary>
  public ModelGenerationOptions ModelGeneration { get; set; } = new();

  /// <summary>
  /// JSON serialization configuration
  /// </summary>
  public SerializationOptions Serialization { get; set; } = new();

  /// <summary>
  /// Operation overrides for adding fixed parameters and modifying behavior
  /// </summary>
  public List<OperationOverride> OperationOverrides { get; set; } = new();

  /// <summary>
  /// Load configuration from a JSON or TOML file
  /// </summary>
  /// <param name="configPath">Path to the configuration file</param>
  /// <returns>The loaded configuration, or default if file doesn't exist</returns>
  public static async Task<GeneratorConfiguration> LoadFromFileAsync(string configPath)
  {
    if (!File.Exists(configPath))
    {
      return new GeneratorConfiguration();
    }

    try
    {
      string content = await File.ReadAllTextAsync(configPath);
      string extension = Path.GetExtension(configPath).ToLowerInvariant();

      GeneratorConfiguration? config = null;

      if (extension == ".toml")
      {
        var tomlOptions = new TomlSerializerOptions
        {
          PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        config = TomlSerializer.Deserialize<GeneratorConfiguration>(content, tomlOptions);
      }
      else
      {
        JsonSerializerOptions options = new()
        {
          PropertyNameCaseInsensitive = true,
          WriteIndented = true,
        };
        config = JsonSerializer.Deserialize<GeneratorConfiguration>(content, options);
      }

      GeneratorConfiguration result = config ?? new GeneratorConfiguration();

      // Load external converter files
      await LoadExternalConvertersAsync(result, Path.GetDirectoryName(configPath) ?? ".");

      return result;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Warning: Failed to load configuration from {configPath}: {ex.Message}");
      return new GeneratorConfiguration();
    }
  }

  /// <summary>
  /// Load external converter files into JsonConverterConfigs
  /// </summary>
  private static async Task LoadExternalConvertersAsync(GeneratorConfiguration config, string configDirectory)
  {
    foreach (JsonConverterConfig converter in config.JsonConverters)
    {
      if (!string.IsNullOrEmpty(converter.ConverterFilePath) && string.IsNullOrEmpty(converter.InlineConverterCode))
      {
        string? converterPath = Path.IsPathRooted(converter.ConverterFilePath)
          ? converter.ConverterFilePath
          : Path.Combine(configDirectory, converter.ConverterFilePath);

        if (File.Exists(converterPath))
        {
          try
          {
            converter.InlineConverterCode = await File.ReadAllTextAsync(converterPath);
          }
          catch (Exception ex)
          {
            Console.WriteLine($"Warning: Failed to load converter file {converterPath}: {ex.Message}");
          }
        }
        else
        {
          Console.WriteLine($"Warning: Converter file not found: {converterPath}");
        }
      }
    }
  }

  /// <summary>
  /// Save configuration to a JSON or TOML file
  /// </summary>
  /// <param name="configPath">Path where to save the configuration</param>
  public async Task SaveToFileAsync(string configPath)
  {
    string extension = Path.GetExtension(configPath).ToLowerInvariant();

    if (extension == ".toml")
    {
      var tomlOptions = new TomlSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
      };
      string toml = TomlSerializer.Serialize(this, tomlOptions);
      await File.WriteAllTextAsync(configPath, toml);
    }
    else
    {
      JsonSerializerOptions options = new()
      {
        WriteIndented = true,
      };
      string json = JsonSerializer.Serialize(this, options);
      await File.WriteAllTextAsync(configPath, json);
    }
  }

  /// <summary>
  /// Convert to legacy GeneratorOptions for compatibility
  /// </summary>
  public GeneratorOptions ToGeneratorOptions()
  {
    return new GeneratorOptions
    {
      InputPath = Specs.FirstOrDefault()?.Path ?? string.Empty,
      OutputPath = OutputPath,
      Namespace = Models.Namespace,
      ProjectName = Models.ProjectName,
      TargetFramework = TargetFramework,
      GenerateNullableReferenceTypes = GenerateNullableReferenceTypes,
      GenerateDataAnnotations = GenerateDataAnnotations,
      ModelGeneration = ModelGeneration,
    };
  }
}