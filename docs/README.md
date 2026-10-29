# Documentation

| Document | Purpose |
| --- | --- |
| [AI usage metering](ai-usage.md) | Internal normalized usage, PostgreSQL records, metric semantics, attribution and limitations |
| [Authoritative tenancy](tenancy.md) | Database-per-tenant foundation, membership context, control-plane boundary and deferred commercial architecture |
| [AI providers](ai-providers.md) | Global defaults, active authoritative tenant overrides, capability states, evidence and limitations |
| [Ollama integration smoke](verification/ai-provider-001b.md) | AI-PROVIDER-001B: shared transport reuse, deterministic validation and real loopback-only Ollama evidence |
| [Authorized READ primitives](ai-read-primitives.md) | AI-API-001: module ownership, persisted authority, routing and composition limits |
| [AI-API-001 verification](verification/ai-api-001.md) | Published v0.12.0 adoption, historical candidate checks and observational Groq results |
| [AI-001 verification](verification/ai-001-dotnet.md) | Provider-neutral assistant, effective READ tooling and published v0.11.1 adoption |
| [Local AI assistant testing](ai-assistant-testing.md) | Authentication, synthetic WMS data and live assistant verification; provider setup links to the canonical reference |
| [WMS inventory foundation](wms-inventory.md) | Implementation-specific public warehouse, reference, movement and inventory APIs |
| [WMS-003 verification](verification/wms-003-dotnet.md) | Item Families, atomic N:M associations and published v0.8.0 adoption |
| [WMS-002 verification](verification/wms-002-dotnet.md) | Storage Attributes, published v0.7.0 adoption, migrations, architecture and live conformance |
| [WMS-001N verification](verification/wms-001-dotnet.md) | Production UoM, v0.6.0 adoption, architecture and validation evidence |
| [Assets demonstration data](assets-demo-data.md) | Opt-in deterministic PostgreSQL dataset, canonical facts, ownership and safe reset |
| [Asset register queries](assets-queries.md) | ASSET-005: PostgreSQL filtering, ordering, offset windows, search and typed JSONB lookup |
| [Asset assignments](assets-assignments.md) | ASSET-004: canonical custody history, atomic actions, PostgreSQL temporal integrity and permissions |
| [Dynamic Asset fields](assets-dynamic-fields.md) | ASSET-003: canonical typed values, stable JSONB, active requirements, safe definition evolution and historical semantics |
| [Assets vertical slice](assets.md) | ASSET-002: static persistent resources through Resource Runtime, lifecycle, permissions, tenant storage and limitations |
| [Usage](usage.md) | Docker development, full validation, networking, isolation and cleanup |
| [Resource Platform](resources.md) | Semantic metadata, canonical hashes, generic execution, authoritative guards, REST/Metadata API, authorized contextual actions, neutral form layout and derived OpenAPI |
| [Contextual resource presentation](resource-presentation-001.md) | RESOURCE-PRESENTATION-001: separate static LIST/CREATE/EDIT/VIEW declarations, Assets two-phase flow and published v0.14.0 conformance |
| [Architecture](architecture.md) | Module boundaries and composition |
| [API internationalization](i18n.md) | English fallback, native request negotiation and module-owned validation resources |
| [I18N-001 verification](verification/i18n-001.md) | Historical native/HTTP totals and conformance before experimental-module removal |
| [Modularity](modularity.md) | Formal module contract, versions, validated catalog, optional initialization, typed contributions and deferred resource/lifecycle architecture |
| [MODULES-001E-DOTNET verification](verification/modules-001e-dotnet.md) | Historical v0.5.0 adoption and original composition/validation evidence |
| [USERS-001 / COLLECTIONS-001 verification](verification/users-001-collections-001.md) | CORE users, current Identity state, concurrent last-admin protection and draft 0.4.0 conformance |
| [Foundation decision](decisions/0001-modular-hexagonal-foundation.md) | Modular and hexagonal foundation |
| [Platform decision](decisions/0002-platform-security-localization.md) | Security and localization |
| [Historical experiment](history/experimental-ai-agent.md) | Superseded experimental design; not the current WMS implementation |
| [CLEANUP-001 verification](verification/cleanup-001.md) | Retired experiments, preserved Core coverage and validation |
| [API tests](../api-tests/README.md) | Local platform and module Bruno suites |
| [Contract revision](../contract.revision) | Selected published shared-contract revision v0.14.0 |
| [Working agreement](../AGENTS.md) | Repository rules |

The backend owns its test infrastructure. Shared OpenAPI and shared Bruno belong
to [ERBAS Contract](https://github.com/alxarafe/erbas-contract).
The [CI workflow](https://github.com/alxarafe/alxarafe-dotnet/actions/workflows/ci.yml)
runs `bin/check`, including shared conformance. Its result is distinct from the
declared release `v0.14.0`. Local and CI validation both verify and test
that same release; historical reports retain their original revisions and results.

[Generic resource contract adoption](generic-resource-contract.md) records formal
published v0.14.0 adoption, immutable identity and direct shared conformance for
Generic Resource Runtime, effective navigation and contextual presentation. Historical candidate evidence
retains its original revisions and results. The old v0.13.0-specific override remains
removed; [explicit candidate verification](usage.md#additional-candidate-verification)
is additive to published conformance and does not change the adopted release.

[Effective navigation](navigation.md) documents module contributions, authorization,
localization and private conditional caching for NAVIGATION-CONTRACT-001.
