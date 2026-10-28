# Repository Guidelines

## Structure and boundaries

This .NET 10/ASP.NET Core modular platform provides shared Health, authentication,
Users, module administration and authorization. Production code is in `src/`,
tests in `tests/`, and documentation in `docs/`. Keep solution and repository-wide
build configuration at the root, with project names consistent with existing modules.

Core must not absorb business domain logic. Functional modules own their domain,
use cases, ports, persistence/migrations, HTTP endpoints, permissions, resources
and tests; they must not depend on removed examples. `erbas.dev.math` and
`erbas.dev.statistics` are exclusively conformance/testing descriptors, registered
only by explicit validation composition, never business capabilities or functional
dependencies. Normal composition includes WMS UoM, Storage Attributes, Item Families and the
AI assistant and Assets modules through the existing pipeline. Host may reference only the WMS,
AI and Assets entry assemblies for explicit composition; framework projects remain
independent of business modules. Further business capabilities require separately
approved designs.

Business persistence must use the central authoritative tenant database resolver,
with an independent business database per tenant. Never fall back to a global
business database or add shared-business tenant discriminator columns. Keep
Platform/Identity/AI usage in the control plane; see `docs/tenancy.md` for membership,
migrations and the deliberately deferred commercial architecture.

## Environment, tools and validation

Docker and Docker Compose are the only supported development environment; do not
expect host .NET SDK, PostgreSQL or `psql`. Run .NET commands in the `app` service.
When a task requires Docker, Compose, a database or another repository runtime
that is missing but normally preparable, prepare it using the repository's
supported commands, including starting services and building required images. A
missing but preparable environment is not by itself a reason to stop. Do not add
host-installed tooling or new fast commands.

Perform only the minimum reconnaissance needed to act safely. Batch independent
inspection where practical; bound output and prefer targeted searches and file
ranges over whole trees, large logs, broad dumps or complete large files. Do not
reread unchanged material without a concrete reason or explore unrelated
alternatives after establishing an implementation path or blocker.

Use the narrowest validation that gives useful evidence during implementation,
then run broader validation, including repository-wide builds, full test suites
and integration/conformance runs, when the task requires it. Do not avoid a
command merely because it takes time; wait for legitimate validation to finish.
Never repeatedly poll a process when the tool can wait efficiently, rerun
unchanged failing commands without a concrete reason, or reread unchanged
material. Never claim validation passed without its actual result; missing or
failed mandatory validation blocks task completion and commit. Delegate work to
the developer or CI only for a real external limitation such as unavailable
credentials, an inaccessible external service, permissions genuinely unavailable
to the agent, an operation requiring human authorization, or infrastructure that
the repository's supported workflow cannot provision. Report actual commands,
results and limitations. Do not add host-installed tooling or new fast commands.

Standard repository commands, used directly as needed:

- `docker compose build`; `docker compose up -d` — prepare the environment.
- `docker compose exec app dotnet restore` — restore dependencies.
- `docker compose exec app dotnet build Alxarafe.sln` — build the solution.
- `docker compose exec app dotnet test Alxarafe.sln` — run the suite; run before a PR.
- `docker compose logs -f app` — inspect logs.

Use the selected .NET test framework, keep tests under `tests/` beside the behavior,
name classes after the system under test and methods descriptively (for example,
`Method_WhenCondition_ExpectedResult`), and add regression coverage for bug fixes.
Keep generated output (`bin/`, `obj/`, coverage reports and local settings) out of
version control.

## Task lifecycle and Git

For each task: obtain explicit approval, implement only its scope, pass applicable
validation, review the diff and documentation impact, then close with a local
atomic commit before requesting approval for the next task. Approval authorizes
staging and the final local commit after validation and scope review; no second
approval is needed for that commit. Use one atomic commit or the minimum required,
with short imperative subjects consistent with history, preferably scoped.

Do not commit incomplete tasks or failed validations; correct and revalidate defects.
Do not start the next task with uncommitted changes from the previous one. If a task
exceptionally starts with another task's pending changes, separate their commits.
Push, PR creation, merge, tag, release, publication and deployment each require
separate express authorization. PRs explain changes and validation, link related
issues, call out configuration or migration steps, and include screenshots or
sample requests for user-facing changes.

## Shared HTTP contract and configuration

OpenAPI and the sole shared conformance Bruno collection belong to erbas-contract;
this backend cannot change observable behavior unilaterally. `/health` is process
liveness only: HTTP 200 `application/json` with exactly `{"status":"ok"}`,
independent of database, module or external-service checks.

`contract.revision` declares the published release implemented, currently `v0.14.0`.
`./bin/check` requires `ERBAS_CONTRACT_DIR`; it checks repository root, expected
GitHub origin, tracked/nonignored cleanliness, release tag object
`22191afb8ab477fef319093b8c212264f4019816`, its target commit
`9919ad3725e50d63e7a19befbe0d81b4d91df0cc` and matching HEAD, preserves ignored
files, and invokes public `bin/test` on the existing validation Docker network.
Never assume a sibling path, fetch, pull, checkout or modify the supplied contract,
or copy its OpenAPI/Bruno into this backend. Local and CI validation must use the
same published release and fail when shared conformance fails. Optional development
verification may additionally use an explicit clean candidate checkout and full SHA;
it must never replace published conformance or change the pin. See
`docs/usage.md#additional-candidate-verification`.

Development publishes API container port 8080 only on
`127.0.0.1:${ERBAS_DOTNET_PORT:-48081}`; follow
`erbas-contract/docs/development-ports.md`. Never fix host ports in HTTP contracts
or Bruno requests, publish PostgreSQL, require the development host port for
validation, or add automatic Dev Container forwarding of API port 8080. Validation
uses Docker network URLs. Never commit credentials, tokens, connection strings or
machine-specific settings; use environment variables or untracked local settings,
and provide safe examples through templates or documentation.

## API internationalization

All user-facing runtime literals use native i18n: English base and Spanish (`en`,
`es`), deterministic fallback and standard `Accept-Language` negotiation including
parent-culture fallback. Do not add language query parameters, custom headers,
mutable global cultures or persistent preferences.

Use `AddLocalization`, `RequestLocalization`, `IStringLocalizer<T>` and
assembly-owned `.resx` resources. Preserve existing validation and native
`ProblemDetails`; localize human text at the HTTP boundary without changing codes,
status or JSON shape. Domain/Application express structured failures and do not
depend on localization, HTTP or request cultures. Use the native Identity error
describer extension for Identity descriptions.

Resources belong to the owning module/feature: Security owns Identity, functional
modules own their messages, and Core owns only transversal HTTP text. Never put
module translations in Host or a global catalog. Neutral `.resx` is English and
`.es.resx` Spanish; use complete parameterized phrases, not concatenated fragments.
Do not introduce translator interfaces, wrappers or a custom translation framework.
Never translate routes, JSON properties, canonical field names/paths, codes, IDs,
permissions/policies, claims, events, module names, configuration/technical keys,
database identifiers or internal operational/development logs. Preserve closed
shared-contract errors. HTTP tests must prove locale selection, fallback, parameter
interpolation, request isolation and stable technical response fields.

## Documentation

After implementation, validation and scope review, assess documentation impact
before the final commit. Update relevant documentation in the same task and future
PR for changes to public behavior, architecture, configuration, usage, development
workflow, API capabilities, repository status or documented limitations; otherwise
do not edit docs merely to record the review. Write public documentation in English.
Functional and technical detail belongs preferably in `docs/`; use separate
functional and documentation commits when useful, but do not automatically create
follow-up DOCS tasks or synchronization after each feature.

Keep README.md a brief, stable landing page for purpose, high-level capabilities,
architecture and ecosystem, essential quick start, onboarding and configuration,
principal limitations, verifiable badges and links. It is not a changelog, task
history, endpoint catalog, WEB-003 slice record or exhaustive internal check list.
Change it only when those high-level descriptions become materially false or
incomplete, not merely for an endpoint, field, validation, test or completed task.

Synchronize cross-repository documentation only for shared policy, contractual or
published version, architecture or ecosystem changes, materially false information
elsewhere, or a significant milestone affecting several repositories. Review related
status references in those cases; obtain approval for changes elsewhere and keep
repository commits separate. Preserve historical references and reports; change
badges only when their verifiable fact changes. Keep CI, local contract conformance
and real full-stack demo verification distinct. Keep detailed task-specific
procedures in existing documentation rather than duplicating them here; do not
create documentation merely to shorten this file.

## Style and engineering simplicity

Use four-space indentation and standard C# naming: `PascalCase` for types, methods
and public members, `camelCase` for locals and parameters, `_camelCase` for private
fields. Prefer nullable references, expression-bodied members where readable and
small focused classes. Add or preserve `.editorconfig`; use `dotnet format` before
submission when formatting rules exist.

Apply KISS, DRY, YAGNI, Occam's razor and reuse before invention: prefer simple,
explicit, maintainable solutions, existing mechanisms and fewer concepts,
dependencies and moving parts. Do not add layers, helpers, factories, interfaces,
services, abstractions, extension points or infrastructure for symmetry or possible
future use. Before a custom abstraction, check native .NET/ASP.NET Core capabilities;
add a layer only for concrete decoupling, modularity or meaningful ergonomics and
briefly document that benefit in code or architectural notes. Prefer idiomatic APIs
over wrappers that merely rename operations. Small duplication is preferable to a
more complex abstraction. Simplicity must not compromise correctness, security,
performance, clarity, testability or contractual behavior.

Give literals with contractual, domain, protocol, configuration or behavioral
meaning one explicit, descriptive source of truth across implementation, tests,
templates, scripts and configuration. This includes meaningful numbers, strings,
paths, URLs, headers, media types, status/error codes, protocol tokens, states,
configuration/environment keys, contract fields, regexes, timeouts, limits, sizes,
thresholds and database/schema identifiers. Prefer, in order: framework constants
or enums; project/domain constants or types; existing configuration; then a narrowly
scoped named constant or enum. Do not duplicate a clearer framework facility.

Do not mechanically name incidental values: obvious local structure, indexes, loop
counters, structural zero/one and arbitrary fixture values may stay inline when they
have no independent meaning or naming reduces clarity. Tests follow the same rule
for significant behavior, but preserve test independence; named test/contract
constants may express deliberate contractual boundaries. Avoid global constants
catalogs and needless abstraction. The shared contract standardizes observable
behavior, not implementation internals: each stack may represent values idiomatically;
it is not a cross-language constants package or generated constants infrastructure.
During scope review explicitly inspect newly introduced meaningful literals and
confirm no magic literals were added.
