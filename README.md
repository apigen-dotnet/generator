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
output_path = "src"
target_framework = "net10.0"
generate_nullable_reference_types = true
generate_data_annotations = true

[[specs]]
path = "specs/my-api.yaml"

[models]
namespace = "Apigen.MyApi.Models"
project_name = "Apigen.MyApi.Models"

[client]
namespace = "Apigen.MyApi.Client"
project_name = "Apigen.MyApi.Client"
client_class_name = "MyApiClient"
generate_client = true
```

`[[specs]]` can be repeated to merge several OpenAPI documents into one client, each
with an optional `path_prefix` that is prepended to every route in that document:

```toml
[[specs]]
path = "specs/identity.json"
path_prefix = "/identity"

[[specs]]
path = "specs/vault.json"
path_prefix = "/api"
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

| API | NuGet package | Source |
|-----|---------------|--------|
| DEGIRO (unofficial) | [`Apigen.Degiro.Unofficial.Client`](https://www.nuget.org/packages/Apigen.Degiro.Unofficial.Client) | [degiro-unofficial](https://github.com/apigen-dotnet/degiro-unofficial) |
| Hetzner (Cloud, Robot, API) | [`Apigen.Hetzner`](https://www.nuget.org/packages/Apigen.Hetzner) | [hetzner](https://github.com/apigen-dotnet/hetzner) |
| Immich | [`Apigen.Immich.Client`](https://www.nuget.org/packages/Apigen.Immich.Client) | [immich](https://github.com/apigen-dotnet/immich) |
| Invoice Ninja v5 | [`Apigen.InvoiceNinja.Client`](https://www.nuget.org/packages/Apigen.InvoiceNinja.Client) | [invoiceninja](https://github.com/apigen-dotnet/invoiceninja) |
| Keycloak Admin | [`Apigen.Keycloak.Admin.Client`](https://www.nuget.org/packages/Apigen.Keycloak.Admin.Client) | [keycloak](https://github.com/apigen-dotnet/keycloak) |
| Paperless-ngx | [`Apigen.PaperlessNgx.Client`](https://www.nuget.org/packages/Apigen.PaperlessNgx.Client) | [paperless-ngx](https://github.com/apigen-dotnet/paperless-ngx) |
| TransIP | [`Apigen.TransIp.Client`](https://www.nuget.org/packages/Apigen.TransIp.Client) | [transip](https://github.com/apigen-dotnet/transip) |
| Vaultwarden | [`Apigen.Vaultwarden.Client`](https://www.nuget.org/packages/Apigen.Vaultwarden.Client) | [vaultwarden](https://github.com/apigen-dotnet/vaultwarden) |
| Vikunja | [`Apigen.Vikunja.Client`](https://www.nuget.org/packages/Apigen.Vikunja.Client) | [vikunja](https://github.com/apigen-dotnet/vikunja) |

All generated clients target `net10.0` — see [docs/target-framework-policy.md](docs/target-framework-policy.md).

## License

MIT - see [LICENSE](LICENSE)
