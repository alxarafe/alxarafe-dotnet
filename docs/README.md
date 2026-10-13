# Documentation

| Document | Purpose |
| --- | --- |
| [Usage](usage.md) | Docker development, full validation, networking, isolation and cleanup |
| [Architecture](architecture.md) | Module boundaries and composition |
| [API internationalization](i18n.md) | English fallback, native request negotiation and module-owned validation resources |
| [I18N-001 verification](verification/i18n-001.md) | Historical native/HTTP totals and conformance before experimental-module removal |
| [Modularity](modularity.md) | Descriptors, API version, sources, availability, registry and future external extension boundaries |
| [MODULES-001E-DOTNET verification](verification/modules-001e-dotnet.md) | Historical v0.5.0 adoption and original composition/validation evidence |
| [USERS-001 / COLLECTIONS-001 verification](verification/users-001-collections-001.md) | CORE users, current Identity state, concurrent last-admin protection and draft 0.4.0 conformance |
| [Foundation decision](decisions/0001-modular-hexagonal-foundation.md) | Modular and hexagonal foundation |
| [Platform decision](decisions/0002-platform-security-localization.md) | Security and localization |
| [Historical experiment](history/experimental-ai-agent.md) | Superseded experimental design; no current functional module |
| [CLEANUP-001 verification](verification/cleanup-001.md) | Retired experiments, preserved Core coverage and validation |
| [API tests](../api-tests/README.md) | Local platform and module Bruno suites |
| [Contract revision](../contract.revision) | Implemented published shared-contract release v0.5.0 |
| [Working agreement](../AGENTS.md) | Repository rules |

The backend owns its test infrastructure. Shared OpenAPI and shared Bruno belong
to [ERBAS Contract](https://github.com/alxarafe/erbas-contract).
The [CI workflow](https://github.com/alxarafe/alxarafe-dotnet/actions/workflows/ci.yml)
runs `bin/check`, including shared conformance. Its result is distinct from the
declared published release `v0.5.0`. Local and CI validation both verify and test
that same release; historical reports retain their original revisions and results.
