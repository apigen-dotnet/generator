using Apigen.Generator.Models;

namespace Apigen.Generator.Tests.Models;

public class GeneratorConfigurationTests
{
  [Fact]
  public void Specs_DefaultsToEmptyList()
  {
    var config = new GeneratorConfiguration();
    Assert.NotNull(config.Specs);
    Assert.Empty(config.Specs);
  }

  [Fact]
  public void InputPath_NoLongerExists()
  {
    var props = typeof(GeneratorConfiguration).GetProperties();
    Assert.DoesNotContain(props, p => p.Name == "InputPath");
  }

  [Fact]
  public void Specs_AcceptsMultipleEntries()
  {
    var config = new GeneratorConfiguration
    {
      Specs = new List<SpecConfiguration>
      {
        new() { Path = "specs/identity.json", PathPrefix = "/identity" },
        new() { Path = "specs/vault.json", PathPrefix = "/api" },
        new() { Path = "specs/public.json", PathPrefix = "/public" },
      }
    };
    Assert.Equal(3, config.Specs.Count);
    Assert.Equal("/identity", config.Specs[0].PathPrefix);
  }

  [Fact]
  public async Task LoadFromFileAsync_ParsesSpecsFromToml()
  {
    string toml = "output_path = \"src\"\ntarget_framework = \"net10.0\"\n\n[[specs]]\npath = \"specs/identity.json\"\npath_prefix = \"/identity\"\n\n[[specs]]\npath = \"specs/vault.json\"\npath_prefix = \"/api\"\n\n[models]\nnamespace = \"Test.Models\"\n";

    string tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.toml");
    try
    {
      await File.WriteAllTextAsync(tempFile, toml);
      var config = await GeneratorConfiguration.LoadFromFileAsync(tempFile);

      Assert.Equal(2, config.Specs.Count);
      Assert.Equal("specs/identity.json", config.Specs[0].Path);
      Assert.Equal("/identity", config.Specs[0].PathPrefix);
      Assert.Equal("specs/vault.json", config.Specs[1].Path);
      Assert.Equal("/api", config.Specs[1].PathPrefix);
    }
    finally
    {
      File.Delete(tempFile);
    }
  }

  [Fact]
  public async Task LoadFromFileAsync_ParsesSingleSpecFromToml()
  {
    string toml = "output_path = \"src\"\n\n[[specs]]\npath = \"specs/api.json\"\npath_prefix = \"\"\n\n[models]\nnamespace = \"Test.Models\"\n";

    string tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.toml");
    try
    {
      await File.WriteAllTextAsync(tempFile, toml);
      var config = await GeneratorConfiguration.LoadFromFileAsync(tempFile);

      Assert.Single(config.Specs);
      Assert.Equal("specs/api.json", config.Specs[0].Path);
      Assert.Equal("", config.Specs[0].PathPrefix);
    }
    finally
    {
      File.Delete(tempFile);
    }
  }

  [Fact]
  public void ToGeneratorOptions_UsesFirstSpecPath()
  {
    var config = new GeneratorConfiguration
    {
      Specs = new List<SpecConfiguration>
      {
        new() { Path = "specs/identity.json", PathPrefix = "/identity" },
        new() { Path = "specs/vault.json", PathPrefix = "/api" },
      },
      OutputPath = "src",
    };

    var options = config.ToGeneratorOptions();
    Assert.Equal("specs/identity.json", options.InputPath);
  }

  [Fact]
  public void ToGeneratorOptions_EmptySpecs_ReturnsEmptyPath()
  {
    var config = new GeneratorConfiguration();
    var options = config.ToGeneratorOptions();
    Assert.Equal(string.Empty, options.InputPath);
  }

  [Fact]
  public void PropertyOverride_UseGenericOptionalType_DefaultsToFalse()
  {
    var over = new PropertyOverride();
    Assert.False(over.UseGenericOptionalType);
  }

  [Fact]
  public void ResolveProjectRoot_ConfigInSpecsDir_ReturnsParent()
  {
    string tempRoot = Path.Combine(Path.GetTempPath(), $"apigen-root-{Guid.NewGuid()}");
    string specsDir = Path.Combine(tempRoot, "specs");
    Directory.CreateDirectory(specsDir);
    try
    {
      string configPath = Path.Combine(specsDir, "foo.toml");
      File.WriteAllText(configPath, "");

      string root = GeneratorConfiguration.ResolveProjectRoot(configPath);

      Assert.Equal(Path.GetFullPath(tempRoot), root);
    }
    finally
    {
      Directory.Delete(tempRoot, recursive: true);
    }
  }

  [Fact]
  public void ResolveProjectRoot_ConfigNotInSpecsDir_ReturnsConfigDir()
  {
    string tempRoot = Path.Combine(Path.GetTempPath(), $"apigen-root-{Guid.NewGuid()}");
    Directory.CreateDirectory(tempRoot);
    try
    {
      string configPath = Path.Combine(tempRoot, "apigen-config.toml");
      File.WriteAllText(configPath, "");

      string root = GeneratorConfiguration.ResolveProjectRoot(configPath);

      Assert.Equal(Path.GetFullPath(tempRoot), root);
    }
    finally
    {
      Directory.Delete(tempRoot, recursive: true);
    }
  }

  [Fact]
  public void ResolveProjectRoot_SpecsDirCaseInsensitive_ReturnsParent()
  {
    string tempRoot = Path.Combine(Path.GetTempPath(), $"apigen-root-{Guid.NewGuid()}");
    string specsDir = Path.Combine(tempRoot, "Specs");
    Directory.CreateDirectory(specsDir);
    try
    {
      string configPath = Path.Combine(specsDir, "foo.toml");
      File.WriteAllText(configPath, "");

      string root = GeneratorConfiguration.ResolveProjectRoot(configPath);

      Assert.Equal(Path.GetFullPath(tempRoot), root);
    }
    finally
    {
      Directory.Delete(tempRoot, recursive: true);
    }
  }

  [Fact]
  public void ResolveProjectRoot_RelativeConfigPath_ReturnsAbsoluteRoot()
  {
    string tempRoot = Path.Combine(Path.GetTempPath(), $"apigen-root-{Guid.NewGuid()}");
    string specsDir = Path.Combine(tempRoot, "specs");
    Directory.CreateDirectory(specsDir);
    string originalCwd = Directory.GetCurrentDirectory();
    try
    {
      string configPath = Path.Combine(specsDir, "foo.toml");
      File.WriteAllText(configPath, "");

      // CWD-based normalization handles macOS /var -> /private/var symlinks
      Directory.SetCurrentDirectory(tempRoot);
      string expectedRoot = Directory.GetCurrentDirectory();

      string root = GeneratorConfiguration.ResolveProjectRoot("specs/foo.toml");

      Assert.Equal(expectedRoot, root);
    }
    finally
    {
      Directory.SetCurrentDirectory(originalCwd);
      Directory.Delete(tempRoot, recursive: true);
    }
  }

  [Fact]
  public void ClientGenerationOptions_UseILogger_DefaultsToTrue()
  {
    var opts = new ClientGenerationOptions();
    Assert.True(opts.UseILogger);
  }

  [Fact]
  public async Task LoadFromFileAsync_ParsesUseILoggerFalseFromToml()
  {
    string toml = "output_path = \"src\"\n\n[[specs]]\npath = \"specs/api.json\"\n\n[models]\nnamespace = \"Test.Models\"\n\n[client]\nuse_ilogger = false\n";

    string tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.toml");
    try
    {
      await File.WriteAllTextAsync(tempFile, toml);
      var config = await GeneratorConfiguration.LoadFromFileAsync(tempFile);

      Assert.False(config.Client.UseILogger);
    }
    finally
    {
      File.Delete(tempFile);
    }
  }

  [Fact]
  public async Task LoadFromFileAsync_ParsesUseILoggerTrueFromToml()
  {
    string toml = "output_path = \"src\"\n\n[[specs]]\npath = \"specs/api.json\"\n\n[models]\nnamespace = \"Test.Models\"\n\n[client]\nuse_ilogger = true\n";

    string tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.toml");
    try
    {
      await File.WriteAllTextAsync(tempFile, toml);
      var config = await GeneratorConfiguration.LoadFromFileAsync(tempFile);

      Assert.True(config.Client.UseILogger);
    }
    finally
    {
      File.Delete(tempFile);
    }
  }

  [Fact]
  public async Task LoadFromFileAsync_ParsesPropertyOverrideUseGenericOptionalTypeFromToml()
  {
    string toml = "output_path = \"src\"\n\n[[specs]]\npath = \"specs/api.json\"\n\n[models]\nnamespace = \"Test.Models\"\n\n[[property_overrides]]\nproperty_filter = \"^(city|country|state)$\"\nmodel_filter = \"^RandomSearchDto$\"\nuse_generic_optional_type = true\n";

    string tempFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.toml");
    try
    {
      await File.WriteAllTextAsync(tempFile, toml);
      var config = await GeneratorConfiguration.LoadFromFileAsync(tempFile);

      Assert.Single(config.PropertyOverrides);
      PropertyOverride over = config.PropertyOverrides[0];
      Assert.Equal("^(city|country|state)$", over.PropertyFilter);
      Assert.Equal("^RandomSearchDto$", over.ModelFilter);
      Assert.True(over.UseGenericOptionalType);
    }
    finally
    {
      File.Delete(tempFile);
    }
  }
}
