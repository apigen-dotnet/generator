using Microsoft.OpenApi;
using Apigen.Generator.Extensions;

namespace Apigen.Generator.Tests.Extensions;

public class OpenApiSchemaExtensionsTests
{
    [Fact]
    public void IsType_String_ReturnsTrue()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String };
        Assert.True(schema.IsType(JsonSchemaType.String));
    }

    [Fact]
    public void IsType_NullableString_ReturnsTrue()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null };
        Assert.True(schema.IsType(JsonSchemaType.String));
    }

    [Fact]
    public void IsType_Integer_DoesNotMatchString()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Integer };
        Assert.False(schema.IsType(JsonSchemaType.String));
    }

    [Fact]
    public void IsNullable_NullableString_ReturnsTrue()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null };
        Assert.True(schema.IsNullable());
    }

    [Fact]
    public void IsNullable_NonNullableString_ReturnsFalse()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String };
        Assert.False(schema.IsNullable());
    }

    [Fact]
    public void IsNullable_NullType_ReturnsFalse()
    {
        var schema = new OpenApiSchema();
        Assert.False(schema.IsNullable());
    }

    [Fact]
    public void GetEffectiveType_NullableInteger_ReturnsInteger()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null };
        Assert.Equal(JsonSchemaType.Integer, schema.GetEffectiveType());
    }

    [Fact]
    public void GetEffectiveType_NullType_ReturnsZero()
    {
        var schema = new OpenApiSchema();
        Assert.Equal((JsonSchemaType)0, schema.GetEffectiveType());
    }

    [Fact]
    public void GetEffectiveType_PlainBoolean_ReturnsBoolean()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Boolean };
        Assert.Equal(JsonSchemaType.Boolean, schema.GetEffectiveType());
    }
}
