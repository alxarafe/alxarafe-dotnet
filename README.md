# Alxarafe.NET

> One shared API contract. An independent .NET implementation.

[![License: Apache-2.0](https://img.shields.io/badge/license-Apache--2.0-blue)](LICENSE)

[![Java backend CI](https://github.com/alxarafe/erbas/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/alxarafe/erbas/actions/workflows/ci.yml)
[![.NET backend CI / shared conformance](https://github.com/alxarafe/alxarafe-dotnet/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/alxarafe/alxarafe-dotnet/actions/workflows/ci.yml)
[![Angular client CI](https://github.com/alxarafe/erbas-client/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/alxarafe/erbas-client/actions/workflows/ci.yml)

Alxarafe.NET is an experimental modular framework built with .NET 10, ASP.NET
Core, EF Core and PostgreSQL. It includes authentication, authorization,
localization, CORE user/module administration, a persistent modular runtime,
authenticated CORE capability discovery/read invocation, a provider-neutral AI
assistant over authorized READ tools and production WMS UoM, Storage
Attribute and Item Family catalogues. WMS also provides a
[warehouse inventory foundation](docs/wms-inventory.md) as a public .NET-specific
extension, with persistent movements and current balances, outside shared
WMS contract conformance. [AI READ primitives](docs/ai-read-primitives.md) are
part of the published v0.12.0 contract implemented by this backend. Real-provider
results remain observational evidence, separate from deterministic conformance.

The [tenancy foundation](docs/tenancy.md) separates Platform/Identity control-plane
storage from an independent business database per tenant. Business execution
requires exactly one active membership plus existing permissions. Tenant selection
for multiple memberships and commercial/branding hierarchy remain deferred.

## ERBAS ecosystem

| Repository | Responsibility |
| --- | --- |
| [erbas-contract](https://github.com/alxarafe/erbas-contract) | Shared OpenAPI and the sole shared Bruno collection |
| [erbas](https://github.com/alxarafe/erbas) | Java/Spring Boot backend; owns its implementation and shared-conformance validation |
| [alxarafe-dotnet](https://github.com/alxarafe/alxarafe-dotnet) | .NET backend implementing Health, AUTH-001, USERS-001, COLLECTIONS-001, CORE module administration, capability discovery/read invocation, AI-001 and WMS UoM/Storage Attributes/Item Families |
| [erbas-client](https://github.com/alxarafe/erbas-client) | Angular 22 client consuming Health, AUTH-001, USERS-001 basic user administration and COLLECTIONS-001 pagination from either backend |

The same client provides authenticated profiles and basic administrator user
management with pagination against either backend. Backend authorization remains
authoritative; the opaque bearer stays backend-bound and memory-only. See the
[client development guide](https://github.com/alxarafe/erbas-client/blob/main/docs/full-stack-development.md)
for behavior, error handling and verification scope.

## Get started

Run full validation with an explicitly supplied clean contract checkout:

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/pinned-erbas-contract ./bin/check
```

The host needs Git, Bash, Docker and Docker Compose. No host .NET, Node.js, Bruno or
PostgreSQL runtime is required. See [usage](docs/usage.md) for development,
networking, test isolation and cleanup. For real local AI assistant testing,
configure the backend-only provider key with `./bin/configure-ai` and follow the
[local AI assistant testing guide](docs/ai-assistant-testing.md).

## Status and documentation

The .NET workflow runs the same `bin/check`, including native tests, local
platform/module suites and shared Bruno. The combined badge is not a separate
Bruno-only result. The Java badge reports Java's own CI, including mandatory
shared conformance against Java's adopted published release.
It does not demonstrate .NET conformance. Angular client CI verifies client
tests, build, runtime and isolated shared API proxies; its
[real dual-backend demo evidence](https://github.com/alxarafe/erbas-client/blob/main/docs/full-stack-development.md#web-002-integration-verification-2026-10-09)
records WEB-002 integration separately. Client CI does not perform backend
shared-conformance validation.

[contract.revision](contract.revision) declares published `v0.12.0`. Local and CI
`./bin/check` verify the supplied checkout's origin, cleanliness, release-tag
target and matching HEAD, then require shared conformance. Coverage includes HTTP
liveness, login, basic CORE user administration, paged collections, dependency-safe
CORE module administration, capability discovery and synchronous READ invocation,
and provider-neutral AI assistant invocation over effective READ capabilities.
See [historical verification](docs/verification/users-001-collections-001.md) and
[AUTH-001](https://github.com/alxarafe/erbas-contract/blob/v0.8.0/docs/core/auth-001.md).
AUTH-002, USERS-001, COLLECTIONS-001, CORE-CAP-001, CORE-CAP-002 and AI-001 are implemented.
The sole shared Bruno collection lives in erbas-contract; Identity registration remains .NET-specific. Identity bearer tokens stay opaque and need not
interoperate with Java. Release checkout requirements are in [usage](docs/usage.md#full-validation).

Normal composition includes erbas.wms with administrator-only UoM, Storage Attribute and Item Family
creation and paged listing, plus atomic family/attribute associations, and the
independently disableable erbas.ai assistant. AI invokes only the caller's effective
READ capabilities and module primitives through controlled routing, and never
exposes WRITE or arbitrary external tools. Testing
uses a deterministic assistant adapter, so CI and shared conformance require no
provider key or external model call; production Groq configuration is opt-in.
Explicit disposable validation additionally registers canonical Math/Statistics
descriptors without mathematical functionality; they are absent in normal execution.
See [AI-001 evidence](docs/verification/ai-001-dotnet.md), [modularity](docs/modularity.md#core-module-administration)
and [WMS-003 evidence](docs/verification/wms-003-dotnet.md).
[WMS-001N](docs/verification/wms-001-dotnet.md) remains historical v0.6.0 evidence.

`POST /api/auth/register` remains a .NET-specific Identity extension outside the
shared contract and Bruno conformance. Portable clients must not depend on it;
it may change or disappear. Contractual account creation uses administrator-only
`POST /api/users`. CORE admin is separate from module permissions.

Browse the [documentation index](docs/README.md) for architecture, decisions,
API scenarios and verification evidence.

## License

Copyright (c) 2026 Alxarafe. Licensed under [Apache-2.0](LICENSE).
