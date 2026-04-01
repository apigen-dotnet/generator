using System.CommandLine;
using Apigen.Generator.Generators;
using Apigen.Generator.Models;
using Apigen.Generator.Services;
using Microsoft.OpenApi;

namespace Apigen.Generator;

internal class Program
{
  static int Main(string[] args)
  {
    Argument<string?> specArg = new("spec")
    {
      Description = "Path or URL to OpenAPI specification",
      Arity = ArgumentArity.ZeroOrOne
    };

    Argument<string?> outputArg = new("output")
    {
      Description = "Output directory",
      Arity = ArgumentArity.ZeroOrOne
    };

    Argument<string?> namespaceArg = new("namespace")
    {
      Description = "Base namespace for generated code",
      Arity = ArgumentArity.ZeroOrOne
    };

    Option<string?> configOption = new("--config", "-c")
    {
      Description = "Configuration file (TOML or JSON)"
    };

    Option<bool> savePatchedOption = new("--save-patched")
    {
      Description = "Write patched spec for diagnosis"
    };

    RootCommand rootCommand = new("OpenAPI to C# client generator")
    {
      specArg,
      outputArg,
      namespaceArg,
      configOption,
      savePatchedOption
    };

    rootCommand.SetAction(parseResult =>
    {
      string? spec = parseResult.GetValue(specArg);
      string? output = parseResult.GetValue(outputArg);
      string? ns = parseResult.GetValue(namespaceArg);
      string? config = parseResult.GetValue(configOption);
      bool savePatched = parseResult.GetValue(savePatchedOption);

      RunGeneratorAsync(spec, output, ns, config, savePatched).GetAwaiter().GetResult();
    });

    Command createConfigCommand = new("create-config")
    {
      Description = "Create a sample configuration file"
    };
    createConfigCommand.SetAction(_ =>
    {
      CreateSampleConfigAsync().GetAwaiter().GetResult();
    });
    rootCommand.Subcommands.Add(createConfigCommand);

    return rootCommand.Parse(args).Invoke();
  }

  private static async Task RunGeneratorAsync(
    string? specPath,
    string? outputPath,
    string? namespaceName,
    string? configPath,
    bool savePatched)
  {
    GeneratorConfiguration config = await LoadConfigurationAsync(configPath);

    if (!string.IsNullOrEmpty(specPath))
    {
      config.Specs = new List<SpecConfiguration>
      {
        new() { Path = specPath, PathPrefix = "" }
      };
    }

    if (!string.IsNullOrEmpty(outputPath))
    {
      config.OutputPath = outputPath;
    }

    if (!string.IsNullOrEmpty(namespaceName))
    {
      config.Models.Namespace = namespaceName;
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

      // Apply spec patches if patches directory exists
      string configDir = configPath != null ? Path.GetDirectoryName(Path.GetFullPath(configPath))! : Directory.GetCurrentDirectory();
      string patchesDir = Path.Combine(configDir, "patches");
      if (Directory.Exists(patchesDir) && Directory.GetFiles(patchesDir, "*.cs").Length > 0)
      {
        SpecPatcher patcher = new();
        patcher.LoadPatches(patchesDir);
        int patchCount = patcher.ApplyPatches(document);

        if (patchCount > 0 && savePatched)
        {
          string firstSpecPath = config.Specs[0].Path;
          string patchedPath = Path.ChangeExtension(firstSpecPath, ".patched.yaml");
          using FileStream fs = File.Create(patchedPath);
          using StreamWriter sw = new(fs);
          OpenApiYamlWriter yamlWriter = new(sw);
          document.SerializeAsV3(yamlWriter);
          Console.WriteLine($"Patched spec written to: {patchedPath}");
        }

        Console.WriteLine($"Found {document.Paths?.Count ?? 0} paths, {document.Components?.Schemas?.Count ?? 0} schemas (after patches)");
      }

      // Generate models
      ModelGenerator modelGenerator = new(options, config);
      Dictionary<string, ModelGenerationDecision>? modelDecisions = await modelGenerator.GenerateModelsAsync(document);

      // Generate API client if requested
      if (config.Client.GenerateClient)
      {
        Console.WriteLine("Generating API client...");
        config.Client.ModelsNamespace = config.Models.Namespace;
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

  private static async Task<GeneratorConfiguration> LoadConfigurationAsync(string? configPath)
  {
    if (string.IsNullOrEmpty(configPath))
    {
      if (File.Exists("apigen-config.toml"))
        configPath = "apigen-config.toml";
      else if (File.Exists("apigen-config.json"))
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

    string path = "apigen-config.json";
    await config.SaveToFileAsync(path);
    Console.WriteLine($"Sample configuration file created: {path}");
    Console.WriteLine("Edit this file to customize code generation settings.");
  }
}
