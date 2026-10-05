# Architecture

Alxarafe.NET is a small, independent modular application framework for .NET. Its first goal is to prove explicit composition, replaceable adapters, and a runnable module.

## Structure and dependency direction

The reusable framework projects are `Alxarafe.Core`, `Alxarafe.Modularity`, `Alxarafe.AspNetCore`, `Alxarafe.EntityFrameworkCore`, `Alxarafe.Security`, `Alxarafe.Security.AspNetCore`, and `Alxarafe.Security.EntityFrameworkCore`. Catalog is split into Domain, Application, Infrastructure, Http, and Module. Host is the global composition root; it names the assemblies to discover and contains no Catalog business logic.

```text
Catalog.Domain <- Catalog.Application <- Catalog.Infrastructure
                                      <- Catalog.Http
Catalog.Module composes both adapters and registers them
Host explicitly discovers Catalog.Module
Host composes Identity and localization
```

Domain owns `Item`, `ItemId`, `Sku`, `ItemName`, and invariants. Application owns normal C# use-case classes and `IItemRepository`. Infrastructure owns `CatalogDbContext`, mappings, the `catalog_items` table, and the EF implementation. HTTP maps Minimal API endpoints and calls Application only. Catalog declares `catalog.items.read` and `catalog.items.create` through the small shared permission contract. The ASP.NET Core security adapter turns declared names into native policies and checks standard bearer claims; Identity persistence stays in the security EF adapter.

## Modules

`IAlxarafeModule` exposes an identifier, declared dependencies, service registration, and ordered initialization. The host passes exact assemblies to `AddModulesFromAssemblies`; no indiscriminate loaded-assembly scan is used. Discovery validates missing dependencies and topologically sorts modules, rejecting cycles. `IHttpModule` is an ASP.NET Core adapter contract, so modularity itself does not depend on endpoint routing.

## Boundaries and deliberate limits

Modules communicate through public contracts or future public events. Direct access from one module to another module’s Infrastructure is forbidden. Multi-tenancy, workflows, CMS, runtime plugins, distributed brokers, CQRS/MediatR, mapping frameworks, authentication, custom CLI, source generators, analyzers, generic repositories, and custom Unit of Work are intentionally excluded from this foundation.

ASP.NET Core native services provide Identity bearer authentication, authorization policies, `RequestLocalization`/`IStringLocalizer`, `AddProblemDetails`/`IExceptionHandler`, and OpenAPI. Domain errors remain semantic; the HTTP boundary resolves user-facing text through resources. Docker is only the reproducible development/runtime environment. PostgreSQL is an adapter, and the Domain remains independent of EF Core, ASP.NET Core, PostgreSQL, security, and localization.
