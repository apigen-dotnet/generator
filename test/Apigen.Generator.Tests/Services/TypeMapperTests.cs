using Microsoft.OpenApi.Models;
using Apigen.Generator.Services;
using Apigen.Generator.Models;

namespace Apigen.Generator.Tests.Services;

public class TypeMapperTests
{
  private readonly TypeMapper _mapper = new();

  #region MapOpenApiTypeToClr

  [Fact]
  public void MapOpenApiTypeToClr_NullSchema_ReturnsObject()
  {
    string result = _mapper.MapOpenApiTypeToClr(null!);

    Assert.Equal("object", result);
  }

  [Theory]
  [InlineData(true, "string?")]
  [InlineData(false, "string")]
  public void MapOpenApiTypeToClr_StringType_ReturnsStringWithNullability(bool useNullable, string expected)
  {
    OpenApiSchema schema = new() { Type = "string" };

    string result = _mapper.MapOpenApiTypeToClr(schema, useNullable);

    Assert.Equal(expected, result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_StringDateTimeFormat_ReturnsNullableDateTime()
  {
    OpenApiSchema schema = new() { Type = "string", Format = "date-time", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("DateTime?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_StringDateTimeFormat_NonNullable_ReturnsDateTime()
  {
    OpenApiSchema schema = new() { Type = "string", Format = "date-time", Nullable = false };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("DateTime", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_StringDateFormat_ReturnsNullableDateOnly()
  {
    OpenApiSchema schema = new() { Type = "string", Format = "date", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("DateOnly?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_StringUuidFormat_ReturnsNullableGuid()
  {
    OpenApiSchema schema = new() { Type = "string", Format = "uuid", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("Guid?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_StringGuidFormat_ReturnsNullableGuid()
  {
    OpenApiSchema schema = new() { Type = "string", Format = "guid", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("Guid?", result);
  }

  [Theory]
  [InlineData(true, true, "byte[]?")]
  [InlineData(true, false, "byte[]")]
  [InlineData(false, false, "byte[]")]
  public void MapOpenApiTypeToClr_StringBinaryFormat_ReturnsByteArray(bool useNullable, bool nullable, string expected)
  {
    OpenApiSchema schema = new() { Type = "string", Format = "binary", Nullable = nullable };

    string result = _mapper.MapOpenApiTypeToClr(schema, useNullable);

    Assert.Equal(expected, result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_IntegerType_ReturnsNullableInt()
  {
    OpenApiSchema schema = new() { Type = "integer", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("int?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_IntegerNonNullable_ReturnsInt()
  {
    OpenApiSchema schema = new() { Type = "integer", Nullable = false };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("int", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_IntegerInt64Format_ReturnsNullableLong()
  {
    OpenApiSchema schema = new() { Type = "integer", Format = "int64", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("long?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_NumberType_ReturnsNullableDecimal()
  {
    OpenApiSchema schema = new() { Type = "number", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("decimal?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_NumberFloatFormat_ReturnsNullableFloat()
  {
    OpenApiSchema schema = new() { Type = "number", Format = "float", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("float?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_NumberDoubleFormat_ReturnsNullableDouble()
  {
    OpenApiSchema schema = new() { Type = "number", Format = "double", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("double?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_BooleanType_ReturnsNullableBool()
  {
    OpenApiSchema schema = new() { Type = "boolean", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("bool?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_BooleanNonNullable_ReturnsBool()
  {
    OpenApiSchema schema = new() { Type = "boolean", Nullable = false };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("bool", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_ArrayType_ReturnsNullableList()
  {
    OpenApiSchema schema = new()
    {
      Type = "array",
      Items = new OpenApiSchema { Type = "string" }
    };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("List<string?>?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_ArrayType_UseNullableFalse_ReturnsListWithoutNullableItems()
  {
    OpenApiSchema schema = new()
    {
      Type = "array",
      Items = new OpenApiSchema { Type = "string" }
    };

    string result = _mapper.MapOpenApiTypeToClr(schema, useNullable: false);

    Assert.Equal("List<string>?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_ObjectType_ReturnsNullableObject()
  {
    OpenApiSchema schema = new() { Type = "object" };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("object?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_ObjectWithAdditionalProperties_ReturnsDictionary()
  {
    OpenApiSchema schema = new()
    {
      Type = "object",
      AdditionalProperties = new OpenApiSchema { Type = "string" }
    };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("Dictionary<string, string?>?", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_UseNullableFalse_OmitsQuestionMark()
  {
    OpenApiSchema schema = new() { Type = "integer", Nullable = true };

    string result = _mapper.MapOpenApiTypeToClr(schema, useNullable: false);

    Assert.Equal("int", result);
  }

  [Fact]
  public void MapOpenApiTypeToClr_UnknownType_ReturnsNullableObject()
  {
    OpenApiSchema schema = new() { Type = "unknown" };

    string result = _mapper.MapOpenApiTypeToClr(schema);

    Assert.Equal("object?", result);
  }

  #endregion

  #region GetPropertyName

  [Theory]
  [InlineData("created_at", "CreatedAt")]
  [InlineData("updated_at", "UpdatedAt")]
  [InlineData("first_name", "FirstName")]
  public void GetPropertyName_SnakeCase_ConvertsToPascalCase(string input, string expected)
  {
    string result = _mapper.GetPropertyName(input);

    Assert.Equal(expected, result);
  }

  [Fact]
  public void GetPropertyName_SpecialCharacterHash_ReplacedWithHash()
  {
    string result = _mapper.GetPropertyName("x5t#S256");

    Assert.Equal("X5THashS256", result);
  }

  [Theory]
  [InlineData("my-property", "MyProperty")]
  [InlineData("content-type", "ContentType")]
  public void GetPropertyName_KebabCase_ConvertsToPascalCase(string input, string expected)
  {
    string result = _mapper.GetPropertyName(input);

    Assert.Equal(expected, result);
  }

  [Fact]
  public void GetPropertyName_EmptyString_ReturnsEmpty()
  {
    string result = _mapper.GetPropertyName("");

    Assert.Equal("", result);
  }

  [Fact]
  public void GetPropertyName_NullString_ReturnsNull()
  {
    string result = _mapper.GetPropertyName(null!);

    Assert.Null(result);
  }

  [Theory]
  [InlineData("@type", "Attype")]
  [InlineData("$ref", "Dollarref")]
  public void GetPropertyName_SpecialPrefixes_ReplacedCorrectly(string input, string expected)
  {
    string result = _mapper.GetPropertyName(input);

    Assert.Equal(expected, result);
  }

  [Theory]
  [InlineData("files[invoice]", "FilesInvoice")]
  [InlineData("files[client]", "FilesClient")]
  [InlineData("data[name]", "DataName")]
  [InlineData("items[0]", "Items0")]
  public void GetPropertyName_BracketNotation_PreservesWordBoundaries(string input, string expected)
  {
    string result = _mapper.GetPropertyName(input);

    Assert.Equal(expected, result);
  }

  [Theory]
  [InlineData("func(arg)", "FuncArg")]
  [InlineData("map{key}", "MapKey")]
  public void GetPropertyName_ParenthesesAndBraces_PreservesWordBoundaries(string input, string expected)
  {
    string result = _mapper.GetPropertyName(input);

    Assert.Equal(expected, result);
  }

  #endregion

  #region GetClassName

  [Fact]
  public void GetClassName_GETPrefix_Stripped()
  {
    string result = _mapper.GetClassName("GETAdminRealms");

    Assert.Equal("AdminRealms", result);
  }

  [Fact]
  public void GetClassName_POSTPrefix_Stripped()
  {
    string result = _mapper.GetClassName("POSTClients");

    Assert.Equal("Clients", result);
  }

  [Fact]
  public void GetClassName_DELETEPrefix_Stripped()
  {
    string result = _mapper.GetClassName("DELETEUser");

    Assert.Equal("User", result);
  }

  [Theory]
  [InlineData("ByID", "ById")]
  [InlineData("ClientID", "ClientId")]
  public void GetClassName_IDAcronym_NormalizedToId(string input, string expected)
  {
    string result = _mapper.GetClassName(input);

    Assert.Equal(expected, result);
  }

  [Fact]
  public void GetClassName_UUIDAcronym_NormalizedToUuid()
  {
    string result = _mapper.GetClassName("UUID");

    Assert.Equal("Uuid", result);
  }

  [Fact]
  public void GetClassName_HTTPSAcronym_NormalizedCorrectly()
  {
    // "HTTPSRequest" → ToDotNetPascalCase splits into HTTPS + Request → "HttpsRequest"
    // NormalizeAcronyms then matches HTTPS → "Https" (5 letters, title-cased)
    string result = _mapper.GetClassName("HTTPSRequest");

    Assert.Equal("HttpsRequest", result);
  }

  [Fact]
  public void GetClassName_TypeNameOverride_Applied()
  {
    List<TypeNameOverride> overrides =
    [
      new TypeNameOverride { OriginalName = "models.Permission", NewName = "Permission" }
    ];
    TypeMapper mapper = new(overrides);

    string result = mapper.GetClassName("models.Permission");

    Assert.Equal("Permission", result);
  }

  [Fact]
  public void GetClassName_TypeNameOverride_PatternBased()
  {
    List<TypeNameOverride> overrides =
    [
      new TypeNameOverride { Pattern = @"^models\.(.+)$", NewName = "$1" }
    ];
    TypeMapper mapper = new(overrides);

    string result = mapper.GetClassName("models.Permission");

    Assert.Equal("Permission", result);
  }

  [Fact]
  public void GetClassName_EmptyString_ReturnsEmpty()
  {
    string result = _mapper.GetClassName("");

    Assert.Equal("", result);
  }

  [Fact]
  public void GetClassName_NullString_ReturnsNull()
  {
    string result = _mapper.GetClassName(null!);

    Assert.Null(result);
  }

  [Fact]
  public void GetClassName_SpecialCharacters_Handled()
  {
    string result = _mapper.GetClassName("my-class#name");

    Assert.Contains("Hash", result);
  }

  [Fact]
  public void GetClassName_NoVerbPrefix_NotStripped()
  {
    string result = _mapper.GetClassName("Getter");

    Assert.Equal("Getter", result);
  }

  #endregion
}
