# ADR 0001: Modular hexagonal foundation

## Decision

Alxarafe.NET starts directly from ASP.NET Core and uses a small, explicit modularity layer around native .NET dependency injection. Each module has Domain, Application, Infrastructure, Http, and Module boundaries. The Host is the only global composition root.

## Rationale

ASP.NET Core provides the host, configuration, DI, health checks, and Minimal APIs needed by the first executable slice. Hexagonal boundaries keep domain rules independent of transport and persistence, while an explicit module assembly list makes discovery predictable and testable. EF Core and Npgsql are adapters, not domain abstractions.

Alxarafe.NET preserves an independent design and freedom of implementation by maintaining a small native core with explicit boundaries.

## Consequences

The first version has more projects than a monolith, but each dependency direction is visible and enforceable. Cross-module collaboration must use public contracts or future public events. Advanced concerns such as distributed events, dynamic loading, workflows, multi-tenancy, and authentication remain outside this foundation until a concrete need justifies them.
