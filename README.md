# Apigen.Generator

OpenAPI to C# client generator. Generates strongly-typed API clients with property overrides, smart enums, and custom JSON converter support.

## Installation

```bash
dotnet tool install --global Apigen.Generator
```

## Usage

```bash
apigen --config my-api.toml
```

## Configuration

Apigen uses TOML configuration files. Example:

```toml
input_path = "specs/my-api.yaml"
output_path = "src"
target_framework = "net9.0"
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
- Request/Response model splitting with deduplication
- Type name overrides for conflict resolution
- Configurable code formatting and naming conventions

## Generated Client Libraries

| API | NuGet Package |
|-----|--------------|
| Invoice Ninja v5 | [`Apigen.InvoiceNinja.Client`](https://github.com/apigen-dotnet/invoiceninja) |
| Keycloak Admin | [`Apigen.Keycloak.Admin.Client`](https://github.com/apigen-dotnet/keycloak) |
| Paperless-ngx | [`Apigen.PaperlessNgx.Client`](https://github.com/apigen-dotnet/paperless-ngx) |
| Vikunja | [`Apigen.Vikunja.Client`](https://github.com/apigen-dotnet/vikunja) |

## License

MIT - see [LICENSE](LICENSE)
