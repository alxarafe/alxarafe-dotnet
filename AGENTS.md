# Repository Guidelines

## Project Structure & Module Organization

This repository implements a .NET 10/ASP.NET Core modular platform, including
shared Health and AUTH-001 login, authorization, Catalog and AiAgent. Production
code lives in `src/`, automated tests in `tests/`, and documentation in `docs/`.
Keep solution files and repository-wide build configuration at the root and use
clear project names consistent with the existing modules.

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

## Task lifecycle

Follow this order for each task: obtain explicit approval, implement only its
scope, pass applicable validations, review the diff, close it with a local atomic
commit, then request and obtain approval before starting the next task.

Task approval also authorizes staging and the final local commit once the task
is implemented, validated and its diff contains only that task's changes. No
second authorization is required solely for that commit. Use one atomic commit,
or the minimum number its structure requires, with clear messages following this
repository's commit guidelines. Preserve all other repository-specific rules.

Do not commit incomplete tasks or tasks with failed validations. Correct and
revalidate defects found before closing a task. Do not start the next task with
uncommitted changes from the previous one. If a task exceptionally starts with
another task's pending changes, separate their commits correctly before continuing.

Task approval does not authorize push, PR creation, merge, tag, release,
publication or deployment; each requires separate express authorization.

## Commit & Pull Request Guidelines

Use short imperative commit subjects consistent with repository history,
preferably scoped when useful (for example, `api: add health endpoint`). Pull
requests should explain the change and validation performed, link related issues,
call out configuration or migration steps, and include screenshots or sample
requests when user-facing behavior changes.

## Security & Configuration Tips

Development publishes API container port 8080 only on
`127.0.0.1:${ERBAS_DOTNET_PORT:-48081}`. Follow the shared local-infrastructure
convention in `erbas-contract/docs/development-ports.md`; never fix host ports in
HTTP contracts or Bruno requests. Keep PostgreSQL unpublished. Validation uses
Docker network URLs and must not require the development host port. Avoid an
additional automatic Dev Container forwarding of API port 8080.

Never commit credentials, tokens, connection strings, or machine-specific settings. Use environment variables or an untracked local settings file, and provide safe examples through configuration templates or documentation.

## Abstraction Guidelines

Before introducing a custom abstraction, check whether .NET or ASP.NET Core already provides a suitable native capability. Add a new layer only when it delivers concrete decoupling, modularity, or meaningful ergonomics; document that benefit briefly in the code or architectural notes. Prefer direct, idiomatic framework APIs over wrappers that merely rename existing types or operations.

## Shared HTTP Contract

OpenAPI and the sole shared conformance Bruno collection belong to erbas-contract.
This backend cannot change their observable behavior unilaterally. The public
`/health` endpoint is HTTP process liveness only: 200 application/json with exactly
`{"status":"ok"}`, independent of database, module or external-service checks.

`contract.revision` declares the implemented published release, currently
`v0.4.0`. `./bin/check` requires `ERBAS_CONTRACT_DIR` and verifies repository root,
the expected GitHub origin, tracked/nonignored cleanliness, release-tag resolution
to `839255ea54610ccf711b29bd096ea294a1ebc3fc` and matching HEAD. Ignored files are
preserved. It invokes the public `bin/test` on the existing validation Docker
network. Never assume a sibling path, fetch, pull, checkout or modify the supplied
contract, or copy its OpenAPI/Bruno into this backend. Local and CI validation
must use the same published release and fail if shared conformance fails.

## Documentation impact review

Every task must review its documentation impact after implementation, validation
and scope review, before the final local commit. If it changes public behavior,
architecture, configuration, usage, development workflow, API capabilities,
repository status or documented limitations, update the relevant documentation
within the same task and the same future pull request. Otherwise, do not modify
documentation merely to record that the review occurred.

README.md is a stable, brief public landing page for project purpose, high-level
capabilities, architecture and ecosystem relationships, essential quick start,
onboarding and configuration, principal limitations, verifiable badges and links
to detailed documentation. It is not a changelog, task history, endpoint catalog,
record of individual WEB-003 slices or exhaustive inventory of internal checks.
Do not change it merely because an endpoint, field, validation or test is added
or changed, or an internal task is completed. Change README only when its
high-level description, onboarding, architecture, essential configuration,
principal capabilities or limitations become materially false or incomplete.

Functional and technical detail belongs preferably in `docs/`. Feature code and
documentation normally belong in the same task and pull request; use separate
functional and documentation commits when that improves review, for example
`feat(...): ...` and `docs: ...`. Do not automatically create a follow-up DOCS task
or coordinated documentation synchronization after each feature.

Cross-repository documentation synchronization is necessary only for a shared
policy change, a contractual or published version change, an architecture or
ecosystem change, materially false information in another repository, or a
significant milestone that actually affects several repositories. Review related
status references when one of these conditions applies; obtain task approval
for necessary changes in other repositories and keep each repository's commits
separate. Preserve historical references and reports as historical; never rewrite
them to appear current. Change badges only when the verifiable fact they represent
changes. Keep CI, local contract conformance and real full-stack demo verification
distinct.

Write public documentation in English. Complete necessary documentation updates
before the final local commit, then obtain approval before starting the next task.
Independent authorization for push, PR creation, merge, tag, release, publication
and deployment remains required.

## Engineering simplicity

Prefer simple, explicit and maintainable solutions.

Apply these principles:

- **KISS** — keep solutions as simple as the requirements allow.
- **DRY** — avoid duplicated logic and duplicated sources of truth.
- **YAGNI** — do not build abstractions, extension points or infrastructure without a concrete current need.
- **Occam's razor** — when several solutions satisfy the requirements equally well, prefer the one with fewer concepts, dependencies and moving parts.
- **Reuse before invention** — prefer existing mechanisms, conventions and components before introducing new ones.

Do not introduce layers, helpers, factories, interfaces, services or abstractions merely for architectural symmetry or possible future use.

Simplicity must not compromise correctness, security, performance, clarity, testability or contractual behavior.

When duplication is small and removing it would create a more complex abstraction, prefer the clearer solution over mechanically applying DRY.

### No magic literals

Do not use magic literals.

Any literal with contractual, domain, protocol, configuration or behavioral
meaning must have one explicit and descriptive source of truth instead of being
scattered through implementation code, tests, templates, scripts or configuration.
This includes, where applicable:

- numeric and string values;
- endpoint paths, URLs and path fragments;
- HTTP header names, media types, HTTP/status/error codes and protocol tokens;
- state names, configuration keys and environment-variable names;
- contract field names and regular expressions with semantic meaning;
- timeouts, limits, sizes and thresholds;
- database/schema identifiers when they carry semantic meaning.

Prefer, in this order:

1. Existing framework/platform constants or enums.
2. Existing project/domain constants or types.
3. Existing configuration.
4. A narrowly scoped named constant or enum introduced for the concrete need.

Do not introduce local constants that merely duplicate an available and clearer
framework facility, such as HTTP status, header or media-type abstractions.
Examples of meaningful values that should not be scattered:

```text
USERS_PAGE_SIZE = 50
USERS_ENDPOINT = "/api/users"
JSON_MEDIA_TYPE = "application/json"
```

Do not mechanically replace every literal with a constant. A literal may remain
inline when it has no independent semantic meaning, its value is obvious from
the immediate local structure, naming it would reduce clarity, or it is incidental
fixture data whose exact value is irrelevant. Examples include simple array
indexes, trivial loop counters, structural zero/one values without domain meaning,
and arbitrary test values used only as distinct fixtures. Do not introduce
pointless constants such as `ZERO`, `ONE` or `TRUE` without a named domain or
protocol concept.

The objective is to eliminate hidden meaning and duplicated sources of truth,
prevent accidental inconsistencies, and improve readability and maintainability.
It is not a global constants catalog, needless abstraction, or replacement of
every string or number. Apply KISS, DRY, YAGNI, Occam's razor and reuse before
invention; this rule must reinforce simplicity.

Tests are subject to the same rule for production, contractual, protocol or
domain behavior. Do not duplicate production-significant literals unnecessarily:
prefer an exported `USERS_PAGE_SIZE` when asserting the production page size.
A deliberate contractual boundary such as 100 may use a named test/contract
constant when that improves semantic clarity. Arbitrary fixture values may
remain local. Do not couple tests unnecessarily to implementation internals;
correctness and test independence remain important.

The shared contract standardizes observable behavior, not implementation
internals. Each stack may represent contractual values idiomatically in its own
constants, enums or types. This rule does not make erbas-contract a general
constants package or require shared cross-language source-code constants or
generated constants infrastructure.

During scope review, explicitly inspect newly introduced meaningful literals and
confirm that no magic literals were added.
