using Microsoft.OpenApi;

namespace Apigen.Generator.Extensions;

internal static class OpenApiSchemaExtensions
{
    /// <summary>
    /// Gets the effective type without the Null flag.
    /// </summary>
    public static JsonSchemaType GetEffectiveType(this OpenApiSchema schema)
    {
        if (schema.Type == null) return 0;
        return schema.Type.Value & ~JsonSchemaType.Null;
    }

    /// <summary>
    /// Returns true if the schema type includes the Null flag (i.e., is nullable).
    /// Replaces the removed schema.Nullable property from OpenApi 1.x.
    /// </summary>
    public static bool IsNullable(this OpenApiSchema schema)
    {
        return schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null);
    }

    /// <summary>
    /// Checks if the schema's effective type (ignoring Null flag) matches the given type.
    /// Replaces `schema.Type == "string"` patterns from OpenApi 1.x.
    /// </summary>
    public static bool IsType(this OpenApiSchema schema, JsonSchemaType type)
    {
        return schema.GetEffectiveType() == type;
    }
}
