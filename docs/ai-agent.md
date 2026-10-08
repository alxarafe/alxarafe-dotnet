# AiAgent

## Current state: 0.1 knowledge persistence

AiAgent remains the umbrella name of this optional module, independent of Catalog. Its current functionality is a preparatory knowledge base, not an operational agent. It stores reusable knowledge; it does not run an AI model. Its five projects follow the existing Domain, Application, Infrastructure, Http, and Module boundaries.

The only entity is `KnowledgeEntry`, with `Id` (Guid), `Question`, and `Answer`. Creation generates an identifier, rejects empty/whitespace question or answer, and trims surrounding whitespace while preserving case and internal text. The EF adapter persists exactly these three required columns in `ai_knowledge` (`uuid`, `text`, `text`). There are no model, embedding, metadata, confidence, status, timestamps, source, conversation, or audit fields. Duplicate questions are allowed; no uniqueness policy is invented.

Application exposes two use cases through a module-owned repository port. Infrastructure implements that port with EF Core/PostgreSQL and owns its context and migrations. The module entry registers services, permissions, and development/test fixtures and applies its own migration. Its declared module dependencies are empty.

| API | Permission | Behavior |
| --- | --- | --- |
| `POST /api/ai/knowledge` | `ai.knowledge.write` | Accepts `question`/`answer`; returns 201, the stored entry, and its Location. |
| `GET /api/ai/knowledge/{id}` | `ai.knowledge.read` | Returns the entry or localized 404 ProblemDetails. |

The HTTP adapter owns `ai.knowledge_not_found` and its Spanish/English resources. Spanish remains the platform default. Empty input uses the existing transversal `validation_error` response (400); no custom exception hierarchy or unused exception handler is needed. Domain/Application have no HTTP, localization, EF, or PostgreSQL dependency.

Search is deliberately deferred to the next knowledge-retrieval phase. This iteration proves storage, retrieval by identifier, permissions, and module boundaries without designing pagination, matching, or ordering prematurely. No semantic search is present.

## First knowledge increment decisions

Knowledge is shared by the whole installation. `ai.knowledge.read` allows reading any entry in that installation, including entries created by another authorized user; `ai.knowledge.write` allows creating entries. There is no owner field, tenant field, or per-user filtering. Future multi-tenancy must be addressed consistently by the platform, outside this increment.

| Field | Domain invariant | Maximum Unicode scalar values |
| --- | --- | --- |
| Question | `KnowledgeEntry.QuestionMaxLength` | 1,000 |
| Answer | `KnowledgeEntry.AnswerMaxLength` | 20,000 |

Both fields are trimmed before checking presence and length. They must contain text after trimming, and the normalized values are persisted and returned. Interior spaces, case, and line breaks remain unchanged; there is no linguistic or Unicode normalization and no deduplication. These limits are functional data invariants, not environment settings.

Length means Unicode scalar values, not UTF-16 code units or grapheme clusters. A supplementary character such as `😀` counts as one; `e` followed by a combining accent counts as two. The domain uses native rune decoding and rejects unpaired UTF-16 surrogates. PostgreSQL `char_length` counts the corresponding stored characters in UTF-8. The API follows the domain validation path: empty or oversized values return HTTP 400 ProblemDetails with the existing `validation_error` code and Spanish/English platform title.

Persistence retains the three `text`/UUID columns and adds `CK_ai_knowledge_Question_Length` and `CK_ai_knowledge_Answer_Length`, requiring stored lengths of 1–1,000 and 1–20,000 respectively. SQL writes outside these bounds are rejected. The application owns trim normalization; direct SQL does not automatically normalize text.

Incremental migration `20261008000100_KnowledgeTextLimits` follows the unchanged `202610060001_InitialKnowledge` migration. A clean database applies both. Existing databases validate their data when adding the checks. If legacy data falls outside the bounds, migration fails without truncating or rewriting it; review and explicitly authorize any data remediation before retrying. No development database is changed by the validation tests.

The generated OpenAPI response/status/security metadata remains known debt for AI-001C. This increment enforces the limits at runtime and does not claim to complete OpenAPI conformance.

## Composition, database configuration, and development

Host knows only `typeof(AiAgentModule).Assembly`, alongside the existing Catalog entry assembly. It does not import AiAgent Application, Domain, Infrastructure, or Http types, declare its permissions, translate its messages, or validate its database. AiAgent checks its own connection and rejects any database other than `alxarafe_ai_test` when the environment is Testing.

- Development connection: `ConnectionStrings__AiAgent`, database `alxarafe_ai` in the supplied Compose configuration.
- Validation connection: the same key, database `alxarafe_ai_test`.
- Migrations: `202610060001_InitialKnowledge`, then `20261008000100_KnowledgeTextLimits`, with a matching current model snapshot and reversible schema operations.

PostgreSQL initialization creates `alxarafe_ai` for a new development volume. Existing volumes do not rerun initialization scripts. Provision the new database explicitly before starting the updated development Host:

```bash
docker compose up -d --wait --wait-timeout 120 postgres
docker compose exec -T postgres psql -U alxarafe -d postgres -v ON_ERROR_STOP=1 -c 'CREATE DATABASE alxarafe_ai'
./bin/up
```

Run the CREATE command only when the database is absent; do not remove development volumes to apply the initialization change. Startup applies the module's EF migration to its configured database.

Development/Testing fixtures are module-owned `ai-reader@example.test` and `ai-writer@example.test`. The reader has only `ai.knowledge.read`; the writer has read/write. Their sample passwords reuse the shared test credential values (`Reader_dev_only_123!` and `Creator_dev_only_123!`) and may be overridden using `AiAgentSeed__ReaderPassword`/`AiAgentSeed__WriterPassword`. The generic platform seed provisions supplied identities only in Development or Testing. Production user provisioning is outside 0.1.

The application can be built and run without AiAgent by omitting its static assembly from `AddAlxarafeModules` and, when desired, removing its Host project reference. Catalog and the platform need no implementation changes or AiAgent database in that composition. Current architecture tests intentionally enumerate the default composition and should be adjusted if that default is changed. This is static composition, not a runtime activation system.

## Tests and validation

`Alxarafe.Modules.AiAgent.Domain.Tests` verifies mandatory text, trim, interior whitespace, exact/excess Unicode scalar limits, identifier generation, and rehydration. `Alxarafe.Modules.AiAgent.IntegrationTests` verifies normalized PostgreSQL readback, installation-wide access across authorized users, permissions (401/403), localized missing entries and length errors, invalid input without persistence, migration/model consistency, and repeated migration application. Its persistence fixtures create and remove uniquely named databases to test clean and incremental migration, preserved earlier data, check constraints, and rejected direct SQL writes with ASCII and supplementary Unicode text. Invalid legacy rows block the new migration without being changed. A separate native generic Host test initializes and uses AiAgent alone, without Catalog or Identity persistence.

Architecture tests verify pure Domain/Application, module-owned permissions, no sibling assembly dependencies in either direction, separate module composition, and the controlled Host entry references. Shared Host source/resource tests now reject AiAgent details as well as Catalog details.

The independent Bruno collection is `api-tests/modules/ai-agent`, with thirteen requests. It explicitly logs in its reader/writer and creates its own knowledge data; it inherits no request state or token from Platform or Catalog. Readback by the different reader demonstrates the shared installation scope. Two extra requests cover oversized question/answer input without repeating the unit boundary matrix. Its environment link reuses shared infrastructure without adding knowledge concepts there. The module's `assert-persistence.sh` verifies OpenAPI route presence, the exact three-column schema, persisted data, both migrations, and the named length constraints.

```bash
./bin/check
./bin/bruno modules/ai-agent
```

The authoritative `bin/check` uses three isolated databases (`alxarafe_test`, `alxarafe_security_test`, `alxarafe_ai_test`) and removes them on exit. It discovers the new Bruno suite and module assertions through the existing runner. GitHub Actions continues to execute that same command.

## Future architectural principle (not implemented)

The future agent must not read another module's tables, DbContext, internal repositories, or Infrastructure, and must not generate arbitrary SQL against the application database. Ownership remains with the module responsible for the operation:

```text
AI Agent
    ↓
capability/tool contract
    ↓
owner module
    ↓
Application / Domain
```

Examples of future controlled capabilities are `catalog.item.get`, `inventory.stock.get`, and `orders.get`. Each owner module would expose and validate its public contract, execute its own application/domain logic, and enforce the caller's permissions. Model-generated requests must never bypass authorization or validation. Future capability execution also needs attributable audit records and explicit human approval for sensitive operations.

There is no capability consumer today, so no `IAiCapability` or other speculative interface is added to production. Future contracts will need stable identifiers, input/output schemas, compatibility rules, execution identity, and authorization semantics based on actual consumers. Naming and ownership of these contracts are open design decisions.

## Core versus optional module/plugin

The complete boundary for future AI-related contracts is not yet settled. The current hypothesis is:

```text
Core / Platform
    minimal generic contracts, only when a real need exists

AiAgent (optional module/plugin)
    knowledge, model providers, prompts, tools, orchestration
```

AiAgent must remain optional: neither Core, Security, Modularity, nor Catalog should acquire implementation references to it. Generic permissions, module contracts, and development fixture options already exist; no AI-specific platform contract is introduced in 0.1.

The direction follows the [existing external plugin design](architecture.md#future-external-plugins-design-only), conceptually:

```text
plugins/
    AiAgent/
        AiAgent.dll
        ...
```

That is a future packaging illustration, not the current assembly layout or installer. Compatibility, dependency resolution/isolation, configuration, migration ownership, and activation still require decisions. The current static module will integrate via the same public platform contracts. No dynamic loading, installation, activation/deactivation, hot reload/unload, `AssemblyLoadContext`, or `AssemblyDependencyResolver` is implemented. An in-process plugin would not be a security sandbox.

## Conceptual roadmap

These labels describe an approximate progression, not a contractual release/version commitment:

| Phase | Direction |
| --- | --- |
| 0.1 | Knowledge persistence and minimal API (current). |
| 0.2 | Knowledge search/retrieval. |
| 0.3 | Model Gateway. |
| 0.4 | OpenAI / Ollama / other provider adapters. |
| 0.5 | Structured output. |
| 0.6 | Read-only capabilities/tools. |
| 0.7 | Capability authorization and audit (API authorization already exists in 0.1). |
| 0.8 | Actions requiring human approval. |
| 0.9 | Orchestration/agent behavior. |

There is no connected model, AI SDK, embeddings, RAG, vector database, tool execution, MCP, autonomous behavior, conversations, conversational memory, AI-specific audit, approval workflow, asynchronous processing, or queue in 0.1.

## Boundary audit and remaining limits

Occurrences of `AiAgent`, `KnowledgeEntry`, `ai_knowledge`, and `ai.knowledge` outside the module have these responsibilities:

| Location | Reason |
| --- | --- |
| Module domain/integration tests and `api-tests/modules/ai-agent` | Module-specific tests, setup, schema/data and HTTP assertions. |
| Architecture tests | Explicit boundary checks and the default static composition allowlist. |
| Host `Program.cs` / project file | Only the static entry assembly/type reference. |
| Solution | Build registration for the five production and two test projects. |
| Compose, PostgreSQL initialization, `bin/test`, `bin/validation-db` | Explicit deployment/test database composition; no knowledge entities, permissions, or domain behavior. |
| Documentation | Current/future architecture and usage instructions. |

There are no AiAgent details in Host resources/exception handling, Core, Security, Modularity, generic adapters, or Catalog. Catalog's implementation and test suite remain unchanged.

Deferred work includes search/pagination, knowledge editing/deletion, production permission provisioning, generic module database installation, dynamic plugins, and all real AI features. MCP would be a later transport adapter and must preserve the same effective identity, permissions, validation, and installation scope rather than bypass API controls. Existing platform seed reconciliation and concurrent bootstrap limitations are unchanged. No timestamps, metadata, or additional fields are added to anticipate those features.
