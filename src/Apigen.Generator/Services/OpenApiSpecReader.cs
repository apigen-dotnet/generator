using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace Apigen.Generator.Services;

public class OpenApiSpecReader
{
  public async Task<OpenApiDocument> ReadSpecificationAsync(string path)
  {
    Stream stream;

    if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
    {
      using HttpClient httpClient = new();
      stream = await httpClient.GetStreamAsync(uri);
    }
    else
    {
      stream = File.OpenRead(path);
    }

    using (stream)
    {
      OpenApiStreamReader reader = new();
      ReadResult? result = await reader.ReadAsync(stream);

      OpenApiDiagnostic? diagnostic = result.OpenApiDiagnostic;
      if (diagnostic?.Errors?.Count > 0)
      {
        string errors = string.Join("\n", diagnostic.Errors.Select(e => e.Message));
        throw new InvalidOperationException($"Failed to parse OpenAPI specification:\n{errors}");
      }

      return result.OpenApiDocument;
    }
  }
}