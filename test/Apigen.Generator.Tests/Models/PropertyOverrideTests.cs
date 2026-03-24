using Apigen.Generator.Models;

namespace Apigen.Generator.Tests.Models;

public class PropertyOverrideTests
{
  [Fact]
  public void Matches_ExactPropertyName_MatchesExact()
  {
    PropertyOverride over = new() { PropertyFilter = "^amount$" };

    Assert.True(over.Matches("amount", "Invoice"));
    Assert.False(over.Matches("total_amount", "Invoice"));
  }

  [Theory]
  [InlineData("created_at", true)]
  [InlineData("updated_at", true)]
  [InlineData("name", false)]
  [InlineData("at_sign", false)]
  public void Matches_PropertyPattern_MatchesSuffix(string propertyName, bool expected)
  {
    PropertyOverride over = new() { PropertyFilter = ".*_at$" };

    bool result = over.Matches(propertyName, "AnyModel");

    Assert.Equal(expected, result);
  }

  [Fact]
  public void Matches_ModelFilter_LimitsToSpecificModel()
  {
    PropertyOverride over = new()
    {
      PropertyFilter = "amount",
      ModelFilter = "^Company$"
    };

    Assert.True(over.Matches("amount", "Company"));
    Assert.False(over.Matches("amount", "Invoice"));
  }

  [Fact]
  public void Matches_ModelFilter_Wildcard_MatchesAll()
  {
    PropertyOverride over = new()
    {
      PropertyFilter = "amount",
      ModelFilter = ".*"
    };

    Assert.True(over.Matches("amount", "Company"));
    Assert.True(over.Matches("amount", "Invoice"));
    Assert.True(over.Matches("amount", "Payment"));
  }

  [Fact]
  public void Matches_NoModelFilter_MatchesAllModels()
  {
    PropertyOverride over = new() { PropertyFilter = "amount" };

    Assert.True(over.Matches("amount", "Company"));
    Assert.True(over.Matches("amount", "Invoice"));
    Assert.True(over.Matches("amount", "Whatever"));
  }

  [Fact]
  public void Matches_OriginalDataTypeFilter_MatchesOnlySpecifiedType()
  {
    PropertyOverride over = new()
    {
      PropertyFilter = "amount",
      OriginalDataType = "number"
    };

    Assert.True(over.Matches("amount", "Invoice", "number"));
    Assert.False(over.Matches("amount", "Invoice", "string"));
  }

  [Fact]
  public void Matches_OriginalFormatFilter_MatchesOnlySpecifiedFormat()
  {
    PropertyOverride over = new()
    {
      PropertyFilter = "avatar",
      OriginalFormat = "binary"
    };

    Assert.True(over.Matches("avatar", "User", "string", "binary"));
    Assert.False(over.Matches("avatar", "User", "string", "date-time"));
  }

  [Fact]
  public void Matches_CombinedFilters_AllMustMatch()
  {
    PropertyOverride over = new()
    {
      PropertyFilter = "^amount$",
      ModelFilter = "^Invoice$",
      OriginalDataType = "number",
      OriginalFormat = "float"
    };

    Assert.True(over.Matches("amount", "Invoice", "number", "float"));
    Assert.False(over.Matches("amount", "Invoice", "number", "double"));
    Assert.False(over.Matches("amount", "Invoice", "string", "float"));
    Assert.False(over.Matches("amount", "Company", "number", "float"));
    Assert.False(over.Matches("total", "Invoice", "number", "float"));
  }

  [Fact]
  public void Matches_CaseInsensitive_MatchesRegardlessOfCase()
  {
    PropertyOverride over = new() { PropertyFilter = "^Amount$" };

    Assert.True(over.Matches("amount", "Invoice"));
    Assert.True(over.Matches("AMOUNT", "Invoice"));
    Assert.True(over.Matches("Amount", "Invoice"));
  }

  [Fact]
  public void Matches_InvalidRegex_FallsBackToStringComparison()
  {
    PropertyOverride over = new() { PropertyFilter = "[invalid" };

    Assert.True(over.Matches("[invalid", "Model"));
    Assert.False(over.Matches("something", "Model"));
  }

  [Fact]
  public void Matches_EmptyPropertyFilter_MatchesAll()
  {
    PropertyOverride over = new() { PropertyFilter = "" };

    Assert.True(over.Matches("anything", "AnyModel"));
  }

  [Fact]
  public void Matches_OriginalDataType_NullDataTypeInput_StillMatches()
  {
    PropertyOverride over = new()
    {
      PropertyFilter = "amount",
      OriginalDataType = "number"
    };

    Assert.True(over.Matches("amount", "Invoice", null));
  }

  [Fact]
  public void Matches_OriginalFormat_NullFormatInput_StillMatches()
  {
    PropertyOverride over = new()
    {
      PropertyFilter = "amount",
      OriginalFormat = "float"
    };

    Assert.True(over.Matches("amount", "Invoice", "number", null));
  }
}
