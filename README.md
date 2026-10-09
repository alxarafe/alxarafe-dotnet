# Alxarafe.NET

> One shared API contract. An independent .NET implementation.

[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)
[![.NET CI / shared Bruno](https://github.com/alxarafe/alxarafe-dotnet/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/alxarafe/alxarafe-dotnet/actions/workflows/ci.yml)
[![Java CI](https://github.com/alxarafe/erbas/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/alxarafe/erbas/actions/workflows/ci.yml)

Alxarafe.NET is an experimental modular framework built with .NET 10, ASP.NET
Core, EF Core and PostgreSQL. It includes authentication, authorization,
localization, a Catalog module and a preparatory AiAgent knowledge base.

## ERBAS ecosystem

| Repository | Responsibility |
| --- | --- |
| [erbas-contract](https://github.com/alxarafe/erbas-contract) | Shared OpenAPI and the sole shared Bruno collection |
| [erbas](https://github.com/alxarafe/erbas) | Independent Java/Spring Boot implementation |
| [alxarafe-dotnet](https://github.com/alxarafe/alxarafe-dotnet) | .NET implementation and its platform/module tests |
| [erbas-client](https://github.com/alxarafe/erbas-client) | Planned shared Angular client |

## Get started

Run full validation with an explicitly supplied clean contract checkout:

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/pinned-erbas-contract ./bin/check
```

The host needs Git, Bash, Docker, Docker Compose and the fixture utilities
listed in [API tests](api-tests/README.md). No host .NET, Node.js, Bruno or
PostgreSQL runtime is required. See [usage](docs/usage.md) for development,
networking, test isolation and cleanup.

## Status and documentation

The .NET workflow runs the same `bin/check`, including native tests, local
platform/module suites and shared Bruno. The combined badge is not a separate
Bruno-only result. The Java badge reports Java's own CI, which does not yet run
shared Bruno.

[contract.revision](contract.revision) pins an unpublished draft. Shared
conformance covers HTTP liveness and login defined by
[AUTH-001](https://github.com/alxarafe/erbas-contract/blob/main/docs/auth-001.md).
The sole shared Bruno collection lives in erbas-contract; registration and module
behavior remain .NET-specific. Identity bearer tokens stay opaque and need not
interoperate with Java. No contract release is published.

Browse the [documentation index](docs/README.md) for architecture, decisions,
API scenarios and the AiAgent roadmap.

## License

Copyright (c) 2026 Alxarafe. Licensed under [Apache-2.0](LICENSE).
