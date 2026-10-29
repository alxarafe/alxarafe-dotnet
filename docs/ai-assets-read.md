# Resource Runtime READ primitives and Assets (AI-ASSETS-READ-001)

## Architecture and contract boundary

The Assistant remains unaware of Assets business rules. Modules explicitly opt
Resource Definitions into READ through the existing `IReadPrimitiveProvider` port.
`ResourceReadPrimitives.List(definition)` generates a descriptor from that same
compiled definition. `ResourceReadPrimitiveExecutor` resolves the frozen
`ResourceRegistry`, binds typed arguments to `ResourceQuery`, calls only
`ResourceRuntime.ListAsync`, and projects the validated page with `ResourceTransport`.

The execution path is:

```text
POST /api/ai/assistant/invocations → AiAssistant → IReadPrimitiveService
→ generic resource executor → ResourceRuntime.ListAsync
→ authorized tenant repository → PostgreSQL
→ canonical business JSON → model → answer + toolsUsed
```

No AI executor references Assets, EF or an `IResourceRepository`. There is no
Assets AI service, privileged principal, alternative tenant, direct database read
or duplicated query implementation. The two reusable adapter classes live in
`Alxarafe.Resources.AspNetCore`, alongside the canonical transport and schema
projection they reuse. Host registers one scoped executor and maps neutral Runtime
failures to existing primitive outcomes. Resource Runtime and its domain adapters
retain validation, permissions, tenant resolution and persistence ownership.

This fits published **erbas-contract v0.14.0**: extensible READ primitives and
closed `{primitiveId, invocationId}` provenance already exist. No public endpoint,
OpenAPI projection, ResourceDefinition, schema migration or contract pin changed.

## Explicit module control

Assets implements `IReadPrimitiveProvider` and selects nine resource IDs from its
existing `AssetsDefinitions`. Each is passed to the generic descriptor builder;
merely registering another ResourceDefinition does not expose it to AI. Other
modules can use the same contribution port/builder; there is no module-name branch
in Host, AI or the resource adapters. Registry validation still rejects invalid or
duplicate IDs, provider names, owners, permissions and open root schemas.

| Primitive ID | Provider function name |
| --- | --- |
| `assets.asset.list` | `assets_asset_list` |
| `assets.asset-type.list` | `assets_asset_type_list` |
| `assets.manufacturer.list` | `assets_manufacturer_list` |
| `assets.asset-model.list` | `assets_asset_model_list` |
| `assets.location.list` | `assets_location_list` |
| `assets.asset-status.list` | `assets_asset_status_list` |
| `assets.asset-field-definition.list` | `assets_asset_field_definition_list` |
| `assets.asset-field-value.list` | `assets_asset_field_value_list` |
| `assets.asset-assignment.list` | `assets_asset_assignment_list` |

IDs are semantic and stable (`resource ID + .list`); provider names replace dots
and hyphens with underscores and obey the existing registry's uniqueness/length
restrictions. There are no opaque aliases. GET was unnecessary: LIST with exact
filters resolves individual records. WRITE and actions are never contributed.

## Schemas and query binding

The root and every filter object use `additionalProperties=false`. Fields,
operators, scalar types, formats, enums, intrinsic/domain constraints and NULL
branches are generated from the ResourceDefinition. Scalar schemas reuse the
existing OpenAPI scalar projector without changing public OpenAPI. Descriptions
include declared return fields and relation target/display metadata; they contain
no manually maintained Asset field list or business instructions.

Arguments are a typed JSON binding of existing ResourceQuery semantics:

```json
{
  "offset": 0,
  "limit": 50,
  "filters": [{"field": "lifecycle", "operator": "eq", "value": "DRAFT"}]
}
```

- Offset is a nonnegative integer. Limit defaults to ResourceQuery's 50 and is
  bounded to 1–100. Pagination is retained in results; totals are preferred for
  counts. There is no COUNT primitive.
- Filters combine with AND, using the same `{field, operator, value}` objects and
  scalar parser as HTTP ResourceTransport. The transport filter parser is shared,
  not copied. Runtime performs authoritative query validation after binding.
- `eq`, `ne`, `lt`, `lte`, `gt`, `gte`, `contains`, `startsWith` are offered only
  where `ResourceQuery.AllowedOperators(field)` permits them. Text fragments keep
  the Runtime's fragment semantics rather than applying full-value length rules.
- Nullable fields have a separate real JSON `null` branch, only for eq/ne. For
  example, `locationId eq null` and `endedAt eq null` use `NullValue`; the string
  `"null"` is not an identifier. Ordered NULL comparisons are rejected.
- Search is offered only for a searchable definition, bounded by the existing
  ResourceQuery search length. Forged fields, types, operators, properties,
  pagination, search or supplied tenant/operation arguments are InvalidRequest.
- Sorting is deliberately not exposed: none of these compositions needs it.
  Existing deterministic repository paging/order is preserved.

Results use the existing canonical `{items, offset, limit, total?}` projection:
plain booleans, exact numbers, strings/dates/UUIDs and null, not CLR/ScalarValue
wrappers. IDs and all declared record fields remain available. Related names are
resolved by composing catalogues, not by invented Asset properties or special joins.
Dynamic field definitions expose their existing `valueField`, required/active flags
and scalar constraints; tools read values without changing AssetDynamicSchema or
AssetDynamicFields.

## Authoritative security and provenance

Descriptor permissions come from each definition's LIST security rule. Effective
discovery and each primitive call re-resolve persisted Identity authority through
`IReadPrimitiveService`. Runtime then independently rechecks module, operation,
request tenant and permissions. No conversation permission snapshot is trusted.
An `ai.assistant.invoke` claim grants no `assets.read` claim.

- The test reader has AI invoke plus assets.read and sees nine Assets primitives.
- The invoke-only identity sees none. A forged call to an existing forbidden
  primitive yields **Denied**, not NotFound.
- Removing assets.read after discovery denies the next tool call in the same
  invocation, using the original bearer. No login/conversation restart is required.
- Disabled Assets primitives disappear; a previously known ID returns Unavailable.
- Tenant membership is the same authoritative request context as HTTP CRUD. A
  newly created record in one tenant is absent in the other, with the same bearer.
  No principal is manufactured by the adapter, and no tenant is accepted as input.

The existing primitive audit records invocation/parent AI IDs, primitive/module,
subject, sequence, duration, outcome and page offset/limit/returned/total/bytes.
Tests check cross-call ordering and common parent identity. No filter values,
business payload, prompts or results are added to logs. Public toolsUsed retains
only the published primitiveId/invocationId variant for successful executions.

## WMS remains separate

The five WMS primitives, their IDs, schemas, executor and `IInventoryStore` path
are unchanged. They retain WMS's existing business-readable stock projection.
Resource primitives instead return canonical Resource Runtime records, including
foreign-key IDs, and compose catalogue reads. Both vocabularies can be offered at
once; permission to invoke AI does not add either module's READ permissions.
Existing WMS differential/composition tests remain, with WMS-specific counts scoped
to their module now that an administrator can discover another READ vocabulary.

## Canonical PostgreSQL composition proofs

Dedicated HTTP integration uses `Assets:DemoData:Enabled=true` and four privately
named PostgreSQL databases. Its TestServer uses the established Development fixture
pattern so the normal Testing guard for fixed validation database names remains
intact. All model calls use an injected controlled `IAiModelClient`, with real login,
HTTP guards, READ routing, repositories and PostgreSQL; there is no network model
provider, EF InMemory store or mocked resource repository.

The source dataset and ownership rules are documented in
[Assets demonstration data](assets-demo-data.md). The proofs obtain facts from
primitive outputs, not from EF queries or constants supplied as answers.

| Question / composition | Observed facts | READ calls |
| --- | --- | --- |
| A: assets list → total | 24 | 1 |
| B: types (LAPTOP) → assets by returned type ID | 8; actual Assistant answer and ordered toolsUsed | 2 |
| C: assets with lifecycle eq DRAFT | 5 | 1 |
| D: assets with locationId eq null | 6 | 1 |
| E: status IN_REPAIR → assets by returned status ID | 3 | 2 |
| F: condition lt 50 | 6 | 1 |
| G: assets + types + manufacturers + models | Type counts 8/5/4/3/4; makers 6 Dell, 3 Lenovo, 7 HP, 3 Samsung, 3 Apple, 2 unspecified; model coherence | 4 in combined consistency proof |
| H: assignments endedAt eq null / ne null | 8 current / 4 closed | 1 per set |
| I: asset tag → current assignment → location | ERB-LT-001 → Main office | 3 |
| J: RAM_GB definition → typed values lt 16 → assets | ERB-LT-002, ERB-LT-005, ERB-LT-006 | 3 |
| K: draft → required definitions → actual values | Minimal draft lacks RAM_GB and ENCRYPTED; partial draft lacks ENCRYPTED | 3 per draft |

The complete read also verifies READY=16, RETIRED=3 and seventeen dynamic values,
including present false booleans. Dynamic query field selection copies the returned
`valueField`; no RAM field or completion rule is added to production AI code.
For K, the proof establishes observable missing definition/value information;
production inference/wording remains the model's responsibility, not a new handler.

Another E2E test creates **Portátil demo IA** via the real Resource API, immediately
reads it through Assistant/tools, switches persisted membership to a second tenant
and observes zero matches, then returns and reads it again. It uses the original
bearer and deletes only its new record. The record is not part of AssetsDemoSeed.

All A–K data needs fit the unchanged budgets. The largest controlled composition
uses **4 tool calls / 5 model turns**. `MaximumToolCalls=8`, `MaximumTurns=8` and
the SystemPrompt remain unchanged. Larger datasets can require more pages than
that budget; no aggregate, count, groupBy or business shortcut was added to hide
that boundary. These controlled proofs do not claim natural-language selection
quality for Groq/Ollama; no optional real-provider request was made.

## Validation and next step

`ResourceReadPrimitiveTests` covers opt-in, non-opted resources, registry identities,
closed generated schemas, all scalar types, operators, null, pagination/search and
absence of WRITE/business/EF dependencies. `ResourceReadPrimitiveApiTests` covers
actual HTTP Assistant composition, canonical PostgreSQL facts, typed projection,
invalid arguments, differential/revoked permissions, modules, tenant isolation,
newly persisted data and payload-free audit/provenance.

Run the normal `./bin/check` with an explicit clean published v0.14.0 checkout.
The dedicated fixture owns and drops its databases; normal validation/conformance
keeps the demo flag false. A separate disposable PostgreSQL proof can run the
focused API tests with console detail through the existing Docker app service:

```bash
docker compose run --rm --no-deps app dotnet test \
  tests/Alxarafe.Host.IntegrationTests --no-build \
  --filter FullyQualifiedName~ResourceReadPrimitiveApiTests \
  --logger 'console;verbosity=normal'
```

PostgreSQL must be prepared with the existing Compose commands. The fixture requires
the existing explicit `ConnectionStrings__Tenant_development` PostgreSQL connection
for database ownership, overrides all four Host connections, opts into demo data,
and cleans up its own unique databases. It neither uses normal development records
nor adds a permanent proof server or dataset.

No GET/WRITE tools, specialized aggregates, arbitrary SQL/HTTP, RAG, embeddings,
MCP, new provider, schema migration, client change or Assets prompt was implemented.
The next step is DEMO-E2E-001: exercise the composed experience and measure actual
provider selection/answer quality under the same permissions and call budget.

## Verification — 2026-10-29

`ERBAS_CONTRACT_DIR=/tmp/assets-demo-contract-v0.14.0 ./bin/check` passed with
**1,209 .NET tests**: 199 architecture, 716 Host integration, 181 Resources and
113 Runtime, zero failures/skips, and build with zero warnings/errors. This adds
22 generic/architectural cases and 40 real HTTP/PostgreSQL cases. OpenAPI,
published v0.14.0 shared conformance, local Bruno, persisted-state/log-secrecy checks
and validation cleanup all passed. Contract tag identity/target remained
`22191afb8ab477fef319093b8c212264f4019816` /
`9919ad3725e50d63e7a19befbe0d81b4d91df0cc`.

After the full check, the separate detailed PostgreSQL proof reran all 40 focused
API cases successfully using fresh private databases and the controlled model.
The real-provider key was explicitly empty. Its output records the 24/8/5/6/3/6
facts, type/manufacturer distributions, assignment/location composition, three
low-RAM laptops, required-field visibility, ordered provenance and immediate
1 → 0 → 1 visibility of the newly created record across tenant changes.
Detailed evidence is local at `/tmp/ai-assets-postgres-proof.log`; full repository
validation is at `/tmp/ai-assets-check.log`. No payload logging was added to the
production audit; detailed fixture evidence is not committed.

The final database ownership query found zero private fixture or normal validation
databases remaining. PostgreSQL was returned to its initial stopped state, with
development data/volumes preserved. No provider, prompt, budget, dependency, seed,
dynamic rule, migration, public protocol, client, contract or Java changes are
part of this task. No live-provider request was made.
