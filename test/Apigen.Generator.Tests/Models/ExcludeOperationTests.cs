using Apigen.Generator.Models;

namespace Apigen.Generator.Tests.Models;

public class ExcludeOperationTests
{
  [Fact]
  public void EmptyFilters_MatchNothing()
  {
    ExcludeOperation exclusion = new();

    Assert.False(exclusion.Matches("Folders_PostPut", "/folders/{id}", "POST"));
  }

  [Fact]
  public void OperationIdFilter_Matches()
  {
    ExcludeOperation exclusion = new() { OperationIdFilter = "_(PostPut|PostDelete)$" };

    Assert.True(exclusion.Matches("Folders_PostPut", "/folders/{id}", "POST"));
    Assert.False(exclusion.Matches("Folders_Put", "/folders/{id}", "PUT"));
  }

  [Fact]
  public void FiltersCombine_AllMustMatch()
  {
    ExcludeOperation exclusion = new()
    {
      OperationIdFilter = "_PostPut$",
      MethodFilter = "^POST$",
      PathFilter = "^/folders"
    };

    Assert.True(exclusion.Matches("Folders_PostPut", "/folders/{id}", "POST"));
    Assert.False(exclusion.Matches("Ciphers_PostPut", "/ciphers/{id}", "POST"));
    Assert.False(exclusion.Matches("Folders_PostPut", "/folders/{id}", "PUT"));
  }

  [Fact]
  public void MissingOperationId_IsTreatedAsEmpty()
  {
    ExcludeOperation exclusion = new() { PathFilter = "^/internal" };

    Assert.True(exclusion.Matches(null, "/internal/debug", "GET"));
  }

  [Fact]
  public void InvalidRegex_FallsBackToExactComparison()
  {
    ExcludeOperation exclusion = new() { OperationIdFilter = "Folders_PostPut(" };

    Assert.True(exclusion.Matches("Folders_PostPut(", "/folders/{id}", "POST"));
    Assert.False(exclusion.Matches("Folders_Put", "/folders/{id}", "PUT"));
  }
}
