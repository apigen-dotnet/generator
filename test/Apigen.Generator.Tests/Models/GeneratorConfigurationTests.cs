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
}
