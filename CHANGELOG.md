# Changelog

## [2.4.0] - 2026-07-28

- Generated project files now emit `<TargetFrameworks>` (plural) instead of `<TargetFramework>`, guarded by `Condition="'$(TargetFrameworks)' == ''"`. Two consequences: multi-targeting (e.g. `net10.0;net12.0`) is now a pure configuration change requiring no generator change, and a repo-level `src/Directory.Build.props` can override the target framework in a way that survives regeneration. MSBuild treats a single-valued `TargetFrameworks` identically to `TargetFramework`, so build output paths are unchanged (`bin/<config>/net10.0/`).
- **Fix**: the default value of `target_framework` was still `net8.0` (in both `GeneratorConfiguration` and `GeneratorOptions`), while every client repo shipped `net10.0` via an explicit override. Any new client generated without an explicit `target_framework` therefore got a project targeting a framework that reaches end of life on 10 November 2026. The default is now `net10.0`.
- Added `docs/target-framework-policy.md`, documenting which target frameworks generated clients ship with, why multi-targeting is off by default, and the dates on which the policy must be revisited.
- **Security**: bumped `Microsoft.OpenApi` and `Microsoft.OpenApi.YamlReader` from 3.5.1 to 3.9.0. 3.5.1 is affected by GHSA-v5pm-xwqc-g5wc (high severity: circular schema references may terminate OpenAPI parsing), patched in 3.5.4.
- **Behavior change in generated models** as a side effect of the parser upgrade: nullable properties that previously got no serialization attribute are now emitted with `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`. Such properties are omitted from the request body when null instead of being serialized as `"prop": null`. For `Patched*` request models (PATCH endpoints) this is a fix — sending an explicit null typically clears the field server-side — but it is a semantic change for existing callers that relied on the previous output. Whether a client is affected depends on its spec; of the current clients only paperless-ngx changed (9 properties across 8 models). Regeneration is required for a client to pick this up.

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
