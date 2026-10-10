# Documentation

| Document | Purpose |
| --- | --- |
| [Usage](usage.md) | Docker development, full validation, networking, isolation and cleanup |
| [Architecture](architecture.md) | Module boundaries and composition |
| [USERS-001 / COLLECTIONS-001 verification](verification/users-001-collections-001.md) | CORE users, current Identity state, concurrent last-admin protection and draft 0.4.0 conformance |
| [Foundation decision](decisions/0001-modular-hexagonal-foundation.md) | Modular and hexagonal foundation |
| [Platform decision](decisions/0002-platform-security-localization.md) | Security and localization |
| [AiAgent](ai-agent.md) | Knowledge base, limits, migrations and roadmap |
| [API tests](../api-tests/README.md) | Local platform and module Bruno suites |
| [Contract revision](../contract.revision) | Exact unpublished shared-contract commit |
| [Working agreement](../AGENTS.md) | Repository rules |

The backend owns its test infrastructure. Shared OpenAPI and shared Bruno belong
to [ERBAS Contract](https://github.com/alxarafe/erbas-contract).
The [CI workflow](https://github.com/alxarafe/alxarafe-dotnet/actions/workflows/ci.yml)
runs `bin/check`, including shared conformance. Its result is distinct from the
implemented draft revision and from a published contract release.
