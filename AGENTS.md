# Repository Guidelines

## Project Structure & Module Organization

Jellyfin is a .NET solution rooted at `Jellyfin.sln`. Main server startup code lives in `Jellyfin.Server/`; HTTP API code is in `Jellyfin.Api/`; shared contracts and core services are split across `MediaBrowser.*`, `Emby.*`, `Jellyfin.Data/`, and `Jellyfin.Server.Implementations/`. Newer database projects are under `src/Jellyfin.Database/`. Tests live in `tests/`, with one project per area such as `Jellyfin.Naming.Tests` or `Jellyfin.Server.Integration.Tests`. Deployment templates are in `deployment/`, fuzzing projects in `fuzz/`, and API documentation assets in `Jellyfin.Server/wwwroot/api-docs/`.

## Build, Test, and Development Commands

- `dotnet restore Jellyfin.sln`: restore NuGet packages using `nuget.config`.
- `dotnet build Jellyfin.sln`: build all projects; warnings are treated as errors.
- `dotnet run --project Jellyfin.Server --webdir /absolute/path/to/jellyfin-web/dist`: run the server with a built Jellyfin web client.
- `dotnet test Jellyfin.sln`: run the full test suite.
- `dotnet test tests/Jellyfin.Naming.Tests/Jellyfin.Naming.Tests.csproj`: run a focused test project.
- `dotnet test Jellyfin.sln --settings tests/coverletArgs.runsettings`: run tests with cobertura coverage collection.

The required SDK is declared in `global.json` and currently targets .NET `10.0.0` with latest-minor roll-forward.

## Coding Style & Naming Conventions

Follow `.editorconfig`: spaces, 4-space indentation for most files, 2 spaces for YAML and XML, UTF-8, LF endings, and final newlines. C# uses nullable reference types, braces on new lines, `var` where preferred by local rules, and sorted `System` usings. Naming conventions are PascalCase for types, members, constants, and local functions; camelCase for locals and parameters; fields use `_camelCase`. StyleCop and custom analyzers are wired through `Directory.Build.props` and `stylecop.json`.

## Testing Guidelines

Place tests in the matching project under `tests/`. Name test classes after the subject, for example `EpisodePathParserTest` or `UserManagerTests`, and keep test data in local `Test Data/` folders when needed. Prefer focused `dotnet test <project>` runs while developing, then run `dotnet test Jellyfin.sln` before submitting broad changes. Integration and OpenAPI tests are under `tests/Jellyfin.Server.Integration.Tests/`.

## Commit & Pull Request Guidelines

Recent history uses short, imperative summaries such as `Update dependency Microsoft.NET.Test.Sdk to 18.5.1` and merge subjects like `Merge pull request #16715 from ...`. Keep commits scoped and descriptive; include issue or PR references when relevant. Pull requests should explain the behavior change, list tests run, link related issues, and include screenshots or API output for user-visible or OpenAPI changes.

## Security & Configuration Tips

Do not commit secrets, local media paths, generated coverage output, or personal server configuration. Keep dependency versions in `Directory.Packages.props` and avoid bypassing analyzer warnings unless the suppression is documented and narrowly scoped.
