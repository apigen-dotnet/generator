using Apigen.Generator.Models;

namespace Apigen.Generator.Tests.Models;

public class SpecConfigurationTests
{
  [Fact]
  public void DefaultValues_AreCorrect()
  {
    var spec = new SpecConfiguration();
    Assert.Equal(string.Empty, spec.Path);
    Assert.Equal(string.Empty, spec.PathPrefix);
  }

  [Fact]
  public void PathPrefix_CanBeEmpty()
  {
    var spec = new SpecConfiguration { Path = "specs/api.json", PathPrefix = "" };
    Assert.Equal("", spec.PathPrefix);
  }

  [Fact]
  public void PathPrefix_StoresValue()
  {
    var spec = new SpecConfiguration { Path = "specs/api.json", PathPrefix = "/api" };
    Assert.Equal("/api", spec.PathPrefix);
  }
}
