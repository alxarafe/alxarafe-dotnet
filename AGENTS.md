# Repository Guidelines

## Project Structure & Module Organization

This repository is currently an empty .NET workspace: no solution, project, source, test, or asset files are checked in yet. When adding implementation, place production code in `src/` (ideally one directory per project), automated tests in `tests/`, and shared documentation or static assets in `docs/` and `assets/`. Keep solution files and repository-wide build configuration at the root. Use clear project names such as `src/Alxarafe.Api/` and `tests/Alxarafe.Api.Tests/`.

## Build, Test, and Development Commands

Docker and Docker Compose are the only supported development environment. The host must not be expected to have the .NET SDK, PostgreSQL, or `psql` installed. Run .NET commands inside the `app` service:

- `docker compose build` — build the development image.
- `docker compose up -d` — start the application and PostgreSQL.
- `docker compose exec app dotnet restore` — restore NuGet dependencies in the container.
- `docker compose exec app dotnet build Alxarafe.sln` — compile the solution in the container.
- `docker compose exec app dotnet test Alxarafe.sln` — execute the test suite in the container.
- `docker compose logs -f app` — inspect application logs.

Keep generated output (`bin/`, `obj/`, coverage reports, and local settings) out of version control.

## Coding Style & Naming Conventions

Use four spaces for indentation and follow standard C# conventions: `PascalCase` for types, methods, and public members; `camelCase` for locals and parameters; and `_camelCase` for private fields. Prefer nullable reference types, expression-bodied members where they improve readability, and small focused classes. Add or preserve `.editorconfig` and use `dotnet format` before submitting changes when formatting rules are available.

## Testing Guidelines

Use the repository’s selected .NET test framework (typically xUnit or NUnit) and keep tests beside the behavior they verify under `tests/`. Name test classes after the system under test and test methods in a descriptive form such as `Method_WhenCondition_ExpectedResult`. Add regression coverage for bug fixes and run `dotnet test` locally before opening a pull request.

## Commit & Pull Request Guidelines

No project Git history is available yet, so no existing commit convention can be inferred. Use short imperative commit subjects, preferably scoped when useful (for example, `api: add health endpoint`). Pull requests should explain the change and validation performed, link related issues, call out configuration or migration steps, and include screenshots or sample requests when user-facing behavior changes.

## Security & Configuration Tips

Never commit credentials, tokens, connection strings, or machine-specific settings. Use environment variables or an untracked local settings file, and provide safe examples through configuration templates or documentation.

## Abstraction Guidelines

Before introducing a custom abstraction, check whether .NET or ASP.NET Core already provides a suitable native capability. Add a new layer only when it delivers concrete decoupling, modularity, or meaningful ergonomics; document that benefit briefly in the code or architectural notes. Prefer direct, idiomatic framework APIs over wrappers that merely rename existing types or operations.
