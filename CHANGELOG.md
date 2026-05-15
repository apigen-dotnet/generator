# Changelog

## [2.3.1] - 2026-05-13

- **Fix**: DELETE (and GET) operations whose OpenAPI spec defines a request body now actually send that body. Previously `HttpClient.DeleteAsync(url, ct)` / `GetAsync(url, ct)` were used unconditionally — there is no overload that accepts content, so the body was silently dropped and the server typically responded `400/406 missing parameter`. Affected operations now generate `SendAsync(new HttpRequestMessage(HttpMethod.Delete|Get, url) { Content = content }, ct)`. Reproduces with TransIP `DELETE /domains/{name}/dns` (`RemoveDnsEntryDomainAsync`) — the `dnsEntry` body is now transmitted correctly. Affected clients: hetzner, immich, transip, vaultwarden, vikunja.
- Extracted shared `EmitRequestBodyContent` helper for body construction (used by POST/PUT/PATCH and now also DELETE/GET).

## [2.3.0] - 2026-05-13

- Generated client operations and interfaces now accept a `CancellationToken cancellationToken = default` parameter and propagate it to all `HttpClient` calls (`GetAsync`/`PostAsync`/`PutAsync`/`PatchAsync`/`DeleteAsync`) and to `HttpContent.ReadAsStringAsync`/`ReadAsStreamAsync`. Non-breaking for existing callers (default value).
- New generated `ApiException` (in `_ApiException.g.cs`) thrown on non-success HTTP responses. Inherits from `HttpRequestException` (non-breaking) and exposes `Method`, `Url`, `ResponseBody`, and `Headers`. Status code is set via the base constructor (`HttpRequestException.StatusCode`), so no shadowing.
- Generated operations now check `response.IsSuccessStatusCode` instead of `EnsureSuccessStatusCode()`. The error body is read only on failure (no memory regression on success — streams stay streamed, large success responses are unaffected).
- When `use_ilogger = true`, operations now distinguish three failure modes via dedicated catches, each with its own EventId before rethrowing:
  - Caller-initiated cancellation (`OperationCanceledException` with `cancellationToken.IsCancellationRequested == true`) → `LogDebugRequestCancelled` (EventId 1004, Debug).
  - `HttpClient.Timeout` (any other `OperationCanceledException`) → `LogErrorRequestTimeout` (EventId 3002, Error).
  - Transport failures (`HttpRequestException` that is not our `ApiException`: DNS, connect, TLS, etc.) → `LogErrorTransportFailure` (EventId 3003, Error).
- `ApiException` now also exposes `ContentHeaders` (`HttpContentHeaders`), giving callers access to `Content-Type`, `Content-Length`, `Content-Disposition`, etc. on error responses in addition to the existing status-line `Headers`.
- Added unit tests covering TOML parsing of `client.use_ilogger` (true/false/default) to lock in current loader behavior.
- Generator now resolves relative paths in the TOML (spec `path`, `output_path`) against the project root regardless of where it is invoked from. Project root = parent of the config file's directory when the config sits in a directory named `specs/`, otherwise the config file's directory itself. This means `dotnet run --project generator/src/Apigen.Generator -- --config transip/specs/transip.toml` now works from the workspace root, not just from `transip/`. New public helper `GeneratorConfiguration.ResolveProjectRoot(configPath)` and 4 unit tests covering the resolution rules.

## [1.0.0] - 2026-03-23

- Initial open-source release
- OpenAPI to C# client generator with property overrides, smart enums, and JSON converter support
