using Microsoft.OpenApi;

namespace Apigen.Generator.Extensions;

internal static class OpenApiSchemaExtensions
{
    /// <summary>
    /// Resolves an IOpenApiSchema to a concrete OpenApiSchema.
    /// In Microsoft.OpenApi 3.x, schema collections (Properties, AllOf, OneOf, etc.)
    /// may contain OpenApiSchemaReference objects instead of OpenApiSchema.
    /// This method transparently resolves references to their target schema.
    /// </summary>
    public static OpenApiSchema ResolveSchema(this IOpenApiSchema schema)
    {
        if (schema is OpenApiSchema concrete)
            return concrete;
        if (schema is OpenApiSchemaReference reference)
            return reference.RecursiveTarget ?? throw new InvalidOperationException($"Unresolved schema reference: {reference.Id}");
        // Fallback: should not happen, but cast will give a clear error
        return (OpenApiSchema)schema;
    }

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
