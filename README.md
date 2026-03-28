# Apigen.Generator

OpenAPI to C# client generator. Generates strongly-typed API clients with property overrides, smart enums, and custom JSON converter support.

## Usage

### From source (recommended)

```bash
git clone https://github.com/apigen-dotnet/generator.git
dotnet run --project generator/src/Apigen.Generator/Apigen.Generator.csproj -- --config my-api.toml
```

### As dotnet tool

```bash
dotnet tool install --global Apigen.Generator
apigen --config my-api.toml
```

## Configuration

Apigen uses TOML configuration files. Example:

```toml
input_path = "specs/my-api.yaml"
output_path = "src"
target_framework = "net10.0"
generate_nullable_reference_types = true
generate_data_annotations = true

[models]
namespace = "Apigen.MyApi.Models"
project_name = "Apigen.MyApi.Models"

[client]
namespace = "Apigen.MyApi.Client"
project_name = "Apigen.MyApi.Client"
client_class_name = "MyApiClient"
generate_client = true
```

## Features

- Generates models and API clients from OpenAPI 3.x specifications
- Property overrides with regex matching to fix API spec inaccuracies
- Custom JSON converters (inline or file-based) for handling API quirks
- Smart enum generation with string serialization
- Binary response support (`Stream`) for file downloads, thumbnails, etc.
- Multipart form-data upload support for file upload endpoints
- Multiple authentication methods (API key, Bearer, Cookie, Basic) via static factory methods
- Request/Response model splitting with deduplication
- Type name overrides for conflict resolution
- Configurable code formatting and naming conventions
- ILogger integration for request/response logging

## Advanced: Per-request authentication

The generated clients bind authentication to the client instance. For scenarios where you need different auth per request (e.g., a proxy serving multiple users), use a `DelegatingHandler`:

```csharp
public class PerRequestAuthHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Your logic to determine the API key for the current request
        string apiKey = GetApiKeyForCurrentUser();
        request.Headers.Add("x-api-key", apiKey);
        return base.SendAsync(request, cancellationToken);
    }
}

// Setup
var handler = new PerRequestAuthHandler { InnerHandler = new HttpClientHandler() };
var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri("https://your-api-instance/api")
};
var client = new ImmichApiClient(httpClient);
```

This works with any generated client — pass a pre-configured `HttpClient` to the constructor.

## Generated Client Libraries

| API | NuGet Package |
|-----|--------------|
| Invoice Ninja v5 | [`Apigen.InvoiceNinja.Client`](https://github.com/apigen-dotnet/invoiceninja) |
| Keycloak Admin | [`Apigen.Keycloak.Admin.Client`](https://github.com/apigen-dotnet/keycloak) |
| Paperless-ngx | [`Apigen.PaperlessNgx.Client`](https://github.com/apigen-dotnet/paperless-ngx) |
| Vikunja | [`Apigen.Vikunja.Client`](https://github.com/apigen-dotnet/vikunja) |
| Immich | [`Apigen.Immich.Client`](https://github.com/apigen-dotnet/immich) |

## License

MIT - see [LICENSE](LICENSE)
