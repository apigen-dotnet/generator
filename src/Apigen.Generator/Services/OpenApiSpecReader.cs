using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Apigen.Generator.Services;

public class OpenApiSpecReader
{
  public async Task<OpenApiDocument> ReadSpecificationAsync(string path)
  {
    string content;

    if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
    {
      using HttpClient httpClient = new();
      content = await httpClient.GetStringAsync(uri);
    }
    else
    {
      content = await File.ReadAllTextAsync(path);
    }

    // Detect format: JSON starts with '{' or '[', otherwise assume YAML
    string trimmed = content.TrimStart();
    string format = trimmed.StartsWith('{') || trimmed.StartsWith('[') ? OpenApiConstants.Json : OpenApiConstants.Yaml;

    var settings = new OpenApiReaderSettings();
    settings.AddYamlReader(); // Register the YAML reader from Microsoft.OpenApi.YamlReader

    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
    var result = await OpenApiDocument.LoadAsync(stream, format, settings);

    OpenApiDiagnostic? diagnostic = result.Diagnostic;
    if (diagnostic?.Errors?.Count > 0)
    {
      string errors = string.Join("\n", diagnostic.Errors.Select(e => e.Message));
      throw new InvalidOperationException($"Failed to parse OpenAPI specification:\n{errors}");
    }

    return result.Document ?? throw new InvalidOperationException("OpenAPI document could not be parsed \u2014 result was null.");
  }

  /// <summary>
  /// Apply a path prefix to all paths in the document.
  /// E.g., prefix="/identity" turns "/connect/token" into "/identity/connect/token"
  /// </summary>
  public static void ApplyPathPrefix(OpenApiDocument document, string prefix)
  {
    if (string.IsNullOrEmpty(prefix) || document.Paths == null)
      return;

    string normalizedPrefix = prefix.TrimEnd('/');

    var originalPaths = document.Paths.ToList();
    document.Paths.Clear();

    foreach (var kvp in originalPaths)
    {
      string newPath = normalizedPrefix + kvp.Key;
      document.Paths[newPath] = kvp.Value;
    }
  }
}
