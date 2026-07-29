# Apigen Release Process

- **NEVER tag a release without verifying CI passes first.** Always run `dotnet build` and `dotnet test` locally before tagging.
- **NEVER tag a release without verifying the NUGET_API_KEY secret is valid** on the target repo. Check with `gh secret list --repo <repo>`.
- NuGet packages are published automatically when a `v*` tag is pushed. The release workflow uses `secrets.NUGET_API_KEY`.
- If the NuGet push fails with 403, the API key is invalid/expired or doesn't have permission for that package ID. Note that a key scoped per package ID does not cover newly added package IDs; a `Apigen.*` glob does.

## Pre-release checklist (MANDATORY before tagging)

1. **Verify CI is green:** `gh run list --repo apigen-dotnet/<repo> --workflow ci.yml --limit 1`
2. **Verify NuGet secret exists:** `gh secret list --repo apigen-dotnet/<repo>` — must show `NUGET_API_KEY`
3. **Verify build locally:** `dotnet build --configuration Release` — must be 0 warnings, 0 errors
4. **Bump version** — client repos: `src/Directory.Build.props`. Generator repo: `<Version>` in `src/Apigen.Generator/Apigen.Generator.csproj` (it has no `Directory.Build.props`). Commit and push
5. **Wait for CI to pass** on the version bump commit
6. **Then tag:** `git tag v<version> && git push origin v<version>`

## Verify the package actually landed

A green Release run is not proof of publication. Check nuget.org afterwards:

```
curl -s https://api.nuget.org/v3-flatcontainer/<lowercase.package.id>/index.json
```

Indexing lags a few minutes behind the push. If the version never appears, inspect the
`Push to NuGet` step in the run log — a step that is `skipped` or missing entirely
publishes nothing while still reporting success.

## What happens when a tag is pushed

1. `release.yml` triggers on `push: tags: ['v*']`
2. It builds, packs, and pushes the `.nupkg` to nuget.org using `NUGET_API_KEY`
3. It creates a GitHub Release with the `.nupkg` as artifact

## What happens when specs/ changes on main

Nothing automatic. `regenerate.yml` was removed from the client repos; a spec change on
`main` does **not** regenerate the client. Regeneration is a manual step, run from the
`apigen-dotnet` superproject:

```
./generate-all.sh            # all clients
./generate-all.sh immich     # one client
```

This means merging a spec update PR leaves the spec and the generated code out of sync
until someone regenerates, reviews the generated diff, and releases the client.

## What happens on a weekly schedule (Monday 18:00 UTC)

1. `update-spec.yml` triggers on `schedule: cron: '0 18 * * 1'`
2. It reads `specs/upstream.toml` for the upstream URL and target file
3. If `enabled = false`, it stops
4. It downloads the upstream spec to the target file
5. If a `specs/patch-spec.py` exists, it runs the patch script (idempotent)
6. If the spec changed, it creates a PR with the updated spec

## Target frameworks

Generated clients target the oldest .NET version still in Active support — currently
`net10.0` only. Do not hand-edit `<TargetFrameworks>` in generated `.csproj` files; set
`target_framework` in the client's `specs/*.toml`, or override with
`<TargetFrameworks>` in the repo's `src/Directory.Build.props`. See
`docs/target-framework-policy.md`.

## Required Secrets per Client Repo

| Secret | Purpose |
|--------|---------|
| `NUGET_API_KEY` | Push packages to nuget.org |

## Required Secrets on Generator Repo

| Secret | Purpose |
|--------|---------|
| `NUGET_API_KEY` | Push the `Apigen.Generator` dotnet tool to nuget.org |
| `DISPATCH_TOKEN` | Notify client repos on generator release (GitHub PAT with repo scope) |
