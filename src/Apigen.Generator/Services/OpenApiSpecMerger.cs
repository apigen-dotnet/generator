using Microsoft.OpenApi.Models;

namespace Apigen.Generator.Services;

/// <summary>
/// Merges multiple OpenAPI documents into a single document.
/// </summary>
public static class OpenApiSpecMerger
{
  public static OpenApiDocument Merge(IEnumerable<OpenApiDocument> documents)
  {
    var docs = documents.ToList();
    if (docs.Count == 0)
      throw new ArgumentException("At least one document is required", nameof(documents));
    if (docs.Count == 1)
      return docs[0];

    // TODO: Full implementation in Task 4
    throw new NotImplementedException("Multi-spec merging will be implemented in Task 4");
  }
}
