# Architecture

Alxarafe.NET is a small, independent modular application framework for .NET. Its first goal is to prove explicit composition, replaceable adapters, and a runnable module.

## Structure and dependency direction

The reusable framework projects are `Alxarafe.Core`, `Alxarafe.Modularity`, `Alxarafe.AspNetCore`, `Alxarafe.EntityFrameworkCore`, `Alxarafe.Security`, `Alxarafe.Security.AspNetCore`, and `Alxarafe.Security.EntityFrameworkCore`. Catalog is split into Domain, Application, Infrastructure, Http, and Module. Host is the global composition root.

> El Host no debe contener conocimiento específico del dominio de ningún módulo. Los módulos deben integrarse mediante contratos públicos de la plataforma.

Platform and Host define contracts and mechanisms; modules implement or extend them. The Host must not translate module exceptions, define module permissions, own module translations, or depend on module implementation types.

```text
Catalog.Domain <- Catalog.Application <- Catalog.Infrastructure
                                      <- Catalog.Http
Catalog.Module composes both adapters and registers them
Host explicitly discovers Catalog.Module (temporary static composition)
Host composes Identity and localization through platform APIs
```

Domain owns `Item`, `ItemId`, `Sku`, `ItemName`, and invariants. Application owns normal C# use-case classes and `IItemRepository`. Infrastructure owns `CatalogDbContext`, mappings, the `catalog_items` table, migrations, and the EF implementation. HTTP maps Minimal API endpoints and calls Application only. Catalog declares `catalog.items.read` and `catalog.items.create` through the shared `IPermissionDefinitionProvider` contract. The ASP.NET Core security adapter turns declared names into native policies and checks standard bearer claims; Identity persistence stays in the security EF adapter.

Each module owns its domain, application, adapters, endpoints, permissions, semantic errors, translations, functional tests, and test artifacts. Modules communicate through public contracts or future public events. Direct access to another module's Infrastructure is forbidden.

## Current module discovery and controlled Host dependency

`IAlxarafeModule` exposes an identifier, declared dependencies, service registration, and ordered initialization. `IHttpModule` is an ASP.NET Core adapter contract, so Modularity itself does not depend on endpoint routing.

Modules currently come from explicitly known assemblies:

```csharp
builder.Services.AddAlxarafeModules(
    typeof(CatalogModule).Assembly);
```

`AddAlxarafeModules` delegates to `ModuleBuilder` and `ModuleGraph`. `ModuleGraph` discovers exported implementations of `IAlxarafeModule` through reflection in those assemblies, constructs them, validates missing dependencies, and sorts them topologically, rejecting cycles. It does not load external assemblies or scan every assembly already loaded in the process.

The only permitted Host-to-Catalog dependency is the project reference to `Alxarafe.Modules.Catalog.Module` and the entry type `CatalogModule` in `Program.cs` to select its assembly. This is temporary static composition, not permission to use other types from that assembly or any Application, Domain, Http, or Infrastructure types. Architecture tests check both compiled assembly/type references and source directives/resources. Framework assemblies may not reference any `Alxarafe.Modules.*` assembly.

Compose, `bin/test`, and `bin/validation-db` still name the two database connections and the fixed isolated database names. These are explicit deployment/test composition, not domain behavior. Catalog validates its own Testing connection before EF can use it; Host validates only the platform Security connection. Making deployment configuration and test database provisioning extensible is a future installation concern.

## Module HTTP errors and localization

Use the native ASP.NET Core `IExceptionHandler` chain. Each module registers its handler in `ConfigureServices`, with the implementation in its HTTP adapter. A handler returns `false` for exceptions it does not own. Module handlers are registered before the platform handler; the Host contains no module exception list. A new static module repeats this pattern without changing the Host's error handling.

`CatalogExceptionHandler` handles Application's `ItemAlreadyExistsException`, sets HTTP 409, and writes native ProblemDetails with `code: catalog.sku_already_exists`, the existing problem type URL, and the shared trace identifier. It resolves the title using `IStringLocalizer<CatalogMessages>` and Catalog's resources. The exception's user-supplied SKU is not included in its response or handler log. Not-found errors remain in Catalog's endpoint adapter.

The platform handler handles only the existing transversal `ArgumentException` validation response (`validation_error`, HTTP 400). It returns `false` for other exceptions without logging them as unhandled prematurely; native middleware owns the final unhandled fallback. Both handlers explicitly set the response status before writing ProblemDetails. Domain and Application have no HTTP status, localization, or ASP.NET Core dependencies. No platform exception hierarchy is introduced.

`RequestLocalization` runs before the exception middleware so the request culture stays active while an exception is converted into a response. It selects `es` or `en` from `Accept-Language`, with Spanish as the default. `PlatformMessages` contains only transversal messages; Catalog owns both missing-item and duplicate-item messages in its default, Spanish, and English resources. See [native exception handling](https://learn.microsoft.com/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0#iexceptionhandler).

## Development/test identities and test ownership

Catalog contributes its reader/creator fixtures and permissions through native `IOptions<DevelopmentUsersOptions>` configuration. The small options data contract belongs to Security and decouples module fixture definitions from Identity persistence. `SecuritySeed` only provisions the supplied users/claims, and runs exclusively in Development or Testing. Catalog does not reference the security EF adapter; no module permissions or credentials are hardcoded in Host. Existing test users, claims, password overrides, and routes are preserved.

The Bruno suites live under `api-tests/platform` (health and authentication) and `api-tests/modules/catalog` (permissions and Catalog scenarios). Each is a separate collection/runner invocation. Shared environment infrastructure has no Catalog entities, errors, or state. Catalog explicitly logs in its reader and creator, creates its own unique SKU, and uses separate token variables. Its create/read sequence is local to that suite; it never consumes a platform suite's runtime state. Module SQL/OpenAPI assertions live with that module. `bin/check` runs .NET tests, all Bruno suites, and module assertions; it remains the authoritative entry point.

Health/registration/login .NET integration tests belong to `Alxarafe.Host.IntegrationTests`; Catalog API tests belong to `Alxarafe.Modules.Catalog.IntegrationTests`. Both use the isolated Testing Host. `bin/test` uses native MSBuild `-m:1` for test execution because each project bootstraps its own Host against the same ephemeral databases; their schema/seed initialization must not race. Neither suite depends on data produced by the other. Duplicate errors have Spanish, English, and default-language regression coverage, plus two Bruno scenarios.

## Future external plugins (design only)

The goal is to allow third parties to build external modules which the Host did not know when it was compiled, for example:

```text
plugins/
    SomePlugin/
        SomePlugin.dll
        ...
```

A future bootstrap flow would be:

```text
discover assembly
→ load assembly
→ locate IAlxarafeModule
→ check compatibility
→ check dependencies
→ check activation
→ register services
→ initialize module
```

`AssemblyLoadContext` and `AssemblyDependencyResolver` are possible native .NET tools for loading and resolving plugin dependencies. They are not implemented now. The platform contracts must retain a shared type identity with the Host; duplicating their assemblies in separate load contexts can make identical-looking interfaces incompatible. Plugin dependency isolation and version conflicts need an explicit policy, including native library resolution. See [.NET plugin loading](https://learn.microsoft.com/dotnet/core/tutorials/creating-app-with-plugin-support).

Activation/deactivation will probably require restarting the process, so service registration, policies, endpoints, and initialization are rebuilt consistently. Hot reload and hot unload are outside the current scope. Disabling a module will also need to validate dependent active modules and must not silently delete its data.

Future compatibility rules must define platform contract/API versions, supported framework/runtime versions, plugin metadata, module versions, and supported dependency ranges. The current graph resolves identifier dependencies and order only; it does not implement version compatibility or activation rules. Dependency resolution should report missing/incompatible dependencies and cycles before registration and initialization.

Installation still needs decisions on package layout, discovery paths, integrity/provenance, configuration and secrets, database provisioning, migration ownership and ordering, upgrade/rollback, and uninstall/data retention. Those decisions should precede implementation of an installer or loader.

An in-process plugin is not a security sandbox. It executes with the Host's privileges and can access process memory, configuration, files, and networks. `AssemblyLoadContext` isolates assembly loading, not untrusted code. If untrusted plugins are required, process/container isolation and a separate communication contract must be designed.

The eventual Host must be able to execute a module that did not exist when the Host was compiled, using public platform contracts rather than module domain knowledge. Static entry assembly references are the current, controlled intermediate state.

## Catalog leakage audit

All source, tests, scripts, API artifacts, and documentation were searched for `Sku`/`SKU`/`sku`, `ItemAlreadyExistsException`, the two semantic error codes, `CatalogMessages`, and `CatalogPermissions`.

| Location | Classification and action |
| --- | --- |
| Catalog Domain/Application | Domain values, invariants, use cases, and semantic exceptions: remain owned by Catalog. |
| Catalog Infrastructure and migrations | Persistence fields, unique index, and schema: remain owned by Catalog. |
| Catalog Http and Module | Endpoints, handlers, resources, permission declarations, fixtures, and database registration: remain owned by Catalog. |
| Catalog .NET tests and `api-tests/modules/catalog` | Functional/domain assertions, input data, tokens, SQL and module OpenAPI checks: remain module tests. |
| Architecture tests | Deliberate references and forbidden-token lists verify the boundary; justified exception. |
| `docs/`, root README and API-test README | Architectural explanations and documented examples: justified documentation. |
| Host handler/resources/seed | Removed exception mapping, semantic error code, translations and permission literals. Only the controlled static entry reference remains in Host. |
| Core, Modularity, ASP.NET Core, EF and Security adapters | No Catalog domain concepts or module implementation references. |
| Platform Bruno and .NET tests/shared test environment | No SKU/item/error/permission knowledge. Platform registration no longer creates a Catalog SKU. |
| Global validation scripts | Catalog SQL/OpenAPI behavior moved to module assertions. Fixed connection/database configuration remains explicit deployment composition. |

There are no `sku` occurrences outside Catalog, Catalog tests, architecture boundary tests, and documentation (excluding generated build output).

## Deliberate limits and remaining debt

No dynamic plugin loader, hot reload/unload, MediatR, AutoMapper/Mapster, new modularity framework, or new NuGet package is added. ASP.NET Core native services provide Identity bearer authentication, authorization policies, localization, ProblemDetails, and OpenAPI. PostgreSQL remains an adapter.

The existing create use case checks SKU uniqueness before inserting. Concurrent inserts can still race and reach the database's unique constraint; mapping that persistence race into the semantic application exception is separate work. Development/test seeds add claims idempotently but do not reconcile revoked permissions, reset existing passwords, or provide production identity administration. Fixed test database provisioning and static module assembly selection remain until installation/activation policies are designed.
