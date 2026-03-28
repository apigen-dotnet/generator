using Apigen.Generator.Generators;
using Apigen.Generator.Models;
using Apigen.Generator.Services;
using System.Linq;
using Microsoft.OpenApi.Models;

namespace Apigen.Generator;

internal class Program
{
  private static async Task Main(string[] args)
  {
    if (args.Length == 0)
    {
      Console.WriteLine("Usage: ApiGenerator <openapi-spec-path> [output-path] [namespace] [--config <config-file>]");
      Console.WriteLine("   or: ApiGenerator --config <config-file>");
      Console.WriteLine();
      Console.WriteLine("Arguments:");
      Console.WriteLine("  openapi-spec-path : Path or URL to OpenAPI specification (required unless using config)");
      Console.WriteLine("  output-path       : Output directory (default: Generated)");
      Console.WriteLine("  namespace         : Base namespace for generated code (default: GeneratedApi.Models)");
      Console.WriteLine();
      Console.WriteLine("Options:");
      Console.WriteLine("  --config <file>   : Use configuration file (default: apigen-config.json if exists)");
      Console.WriteLine("  --create-config   : Create a sample configuration file");
      return;
    }

    // Handle --create-config
    if (args.Contains("--create-config"))
    {
      await CreateSampleConfigAsync();
      return;
    }

    // Load configuration
    GeneratorConfiguration config = await LoadConfigurationAsync(args);

    // Override with command line arguments (skip --config args)
    int configIndex = Array.IndexOf(args, "--config");
    string? configPath = configIndex >= 0 && configIndex < args.Length - 1 ? args[configIndex + 1] : null;
    List<string> positionalArgs = args.Where(a => !a.StartsWith("--") && a != configPath).ToList();

    if (positionalArgs.Count > 0)
    {
      config.Specs = new List<SpecConfiguration>
      {
        new() { Path = positionalArgs[0], PathPrefix = "" }
      };
    }

    if (positionalArgs.Count > 1)
    {
      config.OutputPath = positionalArgs[1];
    }

    if (positionalArgs.Count > 2)
    {
      config.Models.Namespace = positionalArgs[2];
    }

    if (config.Specs.Count == 0 || config.Specs.All(s => string.IsNullOrEmpty(s.Path)))
    {
      Console.WriteLine("Error: No specs specified. Use [[specs]] in your config file or provide a spec path argument.");
      Environment.Exit(1);
    }

    GeneratorOptions options = config.ToGeneratorOptions();

    try
    {
      Console.WriteLine($"Reading OpenAPI specifications ({config.Specs.Count} spec(s))...");
      OpenApiSpecReader reader = new();

      List<OpenApiDocument> specDocuments = new();
      foreach (var spec in config.Specs)
      {
        Console.WriteLine($"  Reading: {spec.Path}");
        OpenApiDocument specDoc = await reader.ReadSpecificationAsync(spec.Path);

        if (!string.IsNullOrEmpty(spec.PathPrefix))
        {
          Console.WriteLine($"    Applying path prefix: {spec.PathPrefix}");
          OpenApiSpecReader.ApplyPathPrefix(specDoc, spec.PathPrefix);
        }

        specDocuments.Add(specDoc);
      }

      OpenApiDocument document;
      if (specDocuments.Count == 1)
      {
        document = specDocuments[0];
      }
      else
      {
        Console.WriteLine($"  Merging {specDocuments.Count} specs...");
        document = OpenApiSpecMerger.Merge(specDocuments);
      }

      Console.WriteLine($"OpenAPI: {document.Info.Title}");
      Console.WriteLine($"Found {document.Paths?.Count ?? 0} paths, {document.Components?.Schemas?.Count ?? 0} schemas");

      // Generate models
      ModelGenerator modelGenerator = new(options, config);
      Dictionary<string, ModelGenerationDecision>? modelDecisions = await modelGenerator.GenerateModelsAsync(document);

      // Generate API client if requested
      if (config.Client.GenerateClient)
      {
        Console.WriteLine("Generating API client...");
        // Set the models namespace from the main config
        config.Client.ModelsNamespace = config.Models.Namespace;
        // Copy response type overrides from main config to client config
        config.Client.ResponseTypeOverrides = config.ResponseTypeOverrides;
        ClientGenerator clientGenerator = new(
          config.Client,
          config.Formatting,
          config.Naming,
          config.OperationOverrides,
          config.TargetFramework,
          config.TypeNameOverrides,
          modelDecisions,
          config.Serialization);
        GeneratedClientCode clientCode = await clientGenerator.GenerateAsync(document, config.OutputPath);
        Console.WriteLine($"API client generated: {Path.Combine(config.OutputPath, config.Client.ProjectName)}");
      }

      Console.WriteLine("Code generation completed successfully!");
      Console.WriteLine($"Models output directory: {Path.Combine(options.OutputPath, options.ProjectName)}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error: {ex.Message}");
      Environment.Exit(1);
    }
  }

  private static async Task<GeneratorConfiguration> LoadConfigurationAsync(string[] args)
  {
    string? configPath = null;

    // Check for --config argument
    int configIndex = Array.IndexOf(args, "--config");
    if (configIndex >= 0 && configIndex < args.Length - 1)
    {
      configPath = args[configIndex + 1];
    }
    // Check for default config files (prefer TOML over JSON)
    else if (File.Exists("apigen-config.toml"))
    {
      configPath = "apigen-config.toml";
    }
    else if (File.Exists("apigen-config.json"))
    {
      configPath = "apigen-config.json";
    }

    if (!string.IsNullOrEmpty(configPath))
    {
      Console.WriteLine($"Loading configuration from: {configPath}");
      return await GeneratorConfiguration.LoadFromFileAsync(configPath);
    }

    return new GeneratorConfiguration();
  }

  private static async Task CreateSampleConfigAsync()
  {
    GeneratorConfiguration config = new()
    {
      Specs = new List<SpecConfiguration>
      {
        new() { Path = "api-docs.yaml", PathPrefix = "" }
      },
      OutputPath = "Generated",
      GenerateNullableReferenceTypes = true,
      GenerateDataAnnotations = true,
      Models = new ModelsOptions
      {
        Namespace = "GeneratedApi.Models",
        ProjectName = "GeneratedApi.Models",
      },
      Formatting = new CodeFormattingOptions
      {
        UseSpaces = true,
        IndentWidth = 2,
      },
    };

    string configPath = "apigen-config.json";
    await config.SaveToFileAsync(configPath);
    Console.WriteLine($"Sample configuration file created: {configPath}");
    Console.WriteLine("Edit this file to customize code generation settings.");
  }
}