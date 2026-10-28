# Authorized READ primitives (AI-API-001)

The backend adopts **published shared v0.13.0** through `contract.revision`, the
immutable release checks in `bin/check` and the matching CI checkout. Deterministic
conformance validates the controlled READ semantics and typed provenance. This
does not establish production readiness or deterministic real-provider quality.

## Capabilities and primitives

Capabilities encapsulate deterministic business knowledge/processes and retain
the existing registry, CORE-CAP-001 discovery and CORE-CAP-002 execution.
Primitives are elementary module-owned READ operations that AI can compose.
They are separate registry entries, not capabilities with renamed IDs.

`Alxarafe.Modularity` owns the neutral primitive descriptor/provider/executor
ports, registry and invocation contracts. Module composition validates owner,
unique operation IDs, provider-safe semantic names, closed input schemas and
permission requirements. WMS owns declarations in `WmsReadPrimitives` and the
executor in `WmsReadPrimitiveExecutor`. It reuses `IInventoryStore` and WMS
filter/pagination/domain rules. AI has no WMS or EF reference.

The Host owns the controlled routing/security adapters. AI combines effective
capabilities and primitives into the model's vocabulary. Model requests can
select only registered names and closed typed arguments. There is no arbitrary
HTTP, URL, method, SQL, filesystem, MCP or privileged service identity tool.

| Stable primitive ID | Provider function name | Filters in addition to offset/limit |
| --- | --- | --- |
| `wms.warehouses.list` | `wms_warehouses_list` | id, code |
| `wms.locations.list` | `wms_locations_list` | id, warehouseId, code |
| `wms.items.list` | `wms_items_list` | id, sku |
| `wms.inventory.list` | `wms_inventory_list` | warehouseId, itemId, locationId, sku |
| `wms.movements.list` | `wms_movements_list` | id, warehouseId, itemId, locationId, sku, from, to |

Filters combine with AND. IDs are UUIDs; code/SKU matching is exact. Timestamps
require offsets/microsecond precision and use `[from,to)`. Defaults are offset 0,
limit 50; maximum 100, using existing COLLECTIONS-001 parsing/envelopes. Item
family filtering is not currently supported. Inventory omits zero balances.

WMS returns business-readable stock projections: item SKU/description/base UoM
and warehouse/location codes with inventory and movements. It batch-resolves
those labels through the existing application store, so AI receives no foreign
key-only rows and the executor does not issue N+1 queries. This is not an
analytic aggregate or a shortcut for a business question.

## Persisted authority and development identities

`CurrentSecurityContext` re-reads Identity user existence and persisted claims
with untracked queries. Disabled identities are rejected. No privileged principal
is manufactured: the resolved principal contains the same subject and only
persisted claims. Both initial primitive discovery and each individual execution
resolve current authority. The router then rechecks operation policy and owning
module state; it never trusts a conversation snapshot or merely a supplied claim.
Capability effective discovery also uses this persisted resolution.

The development WMS reader still receives the same four business READ claims.
When AI is composed in Development, its module explicitly contributes
`ai.assistant.invoke` to configured generic `DevelopmentUsersOptions` identities.
No WMS permission is added by AI. Production/Testing do not use this development
contribution. Normal authentication, CORE administrator rights and module gates
remain authoritative; names such as admin/user are not authorization rules.

| Effective operation | Development admin | Restricted development user |
| --- | --- | --- |
| AI invoke | Allowed | Allowed |
| Warehouses, locations, items, inventory primitive | Offered | Offered |
| Movements primitive | Offered | Omitted before model presentation |
| WMS WRITE HTTP APIs | Existing authorization | Forbidden |
| WRITE through AI | Never offered/executed | Never offered/executed |

A direct or forged request for a registered unauthorized primitive is `Denied`,
including when a persisted permission is revoked after model discovery. It is
not `NotFound`, a missing capability or a product gap. Unknown registration is
`NotFound`; module state is `Unavailable`; input/storage failures are separate
outcomes. Public access limitations use owner-localized functional text without
permission names, operation IDs, endpoints or configuration instructions.

## Composition, limits and provenance

The prompt supplies current server UTC date, requires registered-state grounding
and encourages filters, complete pages and composition rather than N+1. General
knowledge remains possible for general questions. The natural-language answer
must describe registered balances, not unobserved physical stock. Server guards
replace technical references with a functional limitation rather than disclose
internal IDs in the answer.

The initial budget remains **8 tool calls and 8 model turns**. Every attempted
primitive counts, including failure. Capability and primitive calls share the
same budget. Exhaustion is a safe execution failure; loops are bounded. No
stagnation/rotation/last-movement analytic primitive or new capability is present.
The real composition cost must be measured before changing limits or promoting
business logic into a specialized capability.

Public `toolsUsed` preserves execution order using exactly one closed variant:
`{capabilityId, invocationId}` retains its CORE-CAP-002 meaning;
`{primitiveId, invocationId}` identifies a unique successful audited primitive
execution. IDs are distinct from the AI invocation and every other tool execution.
Failed/denied attempts are audited internally and absent from public provenance.
Arguments, outputs, provider metadata and reasoning are not returned there.

Primitive audit includes parent AI invocation, correlation, operation, owning
module, subject, sequence, duration and logical outcome. Only bounded paging
metadata (offset, limit, records returned, total, serialized byte count) is added for composition-cost
measurement; prompts, filter values, results, credentials and chain-of-thought
are not logged. AI final outcome and existing CORE audit allow cross-kind tracing.
Repeated successful calls and failures remain visible rather than silently hidden.
There is no dashboard, automatic promotion policy or new public admin endpoint.

The shared OpenAI-compatible transport retains semantic names and null omission.
Groq-specific policy retains the single `tool_use_failed` completion retry.
A separate bounded Groq retry (at most once)
honors an explicit `Retry-After` only for provider 429/503 responses, capped at
60 seconds. Already obtained tool results are retained; completed operations
are not replayed as part of transport recovery. No WRITE retry mechanism is
introduced. HTTP 413/token-rate failures are surfaced as safe assistant errors;
the model may still produce a large request while composing broad histories.

The provider transport losslessly encodes uniform list pages as columns and
rows, with constant fields stored once for every row. It retains every value,
record (including duplicate records), and page metadata. Only the provider
message representation changes; application results and HTTP responses do not.
Nonuniform, single-row or empty lists retain their original representation.
Debug-level size diagnostics contain only numeric byte/message counts, never
prompts or data. Stateless completions retain prior results once and include the
effective tool schemas so the model can select its next authorized operation.

An invalid primitive argument produces a bounded model-visible validation result
instead of immediately failing the assistant. The model can resolve an unknown
identifier from a list and correct its arguments. Invalid attempts consume the
same eight-call budget and remain audited; they do not enter successful public
provenance. Authorization denials retain their distinct handling.

When available facts do not support an answer about current ERP state, the
model is instructed to return an internal unresolved signal. The application
converts it to a localized functional limitation; the marker and tool details
are not exposed. This remains general handling, not a question-specific rule.

## Published release verification

Run normal `bin/check` with an explicit clean checkout of the published v0.13.0
release. It verifies the expected origin, immutable tag object, target commit,
matching HEAD and clean state before executing conformance. Local validation and
CI use that same release. To run conformance separately, use the published
checkout against a fresh backend-owned validation Host/database/network:

```sh
ERBAS_TEST_EMAIL=... ERBAS_TEST_PASSWORD=... \
  /absolute/path/to/published-v0.13.0-contract/bin/test --network NETWORK http://HOST:8080
```

No copied OpenAPI/Bruno, invented tag or consumer revision override is needed.
The backend owns readiness, disposable databases and cleanup; the contract runner
only examines the supplied URL. WMS-004 HTTP endpoints remain implementation-
specific; generic AI conformance does not imply Java/WMS-004 parity.

For real provider evaluation, load the independent dataset through its `seed.sh`
with a supplied URL and normal administrator login, then query the assistant as
both normally seeded identities. Keep Groq credentials in local secret configuration
and never print them. Compare answers with `stock.json`/`expected-ai-cases.json`;
record operations, counts, paging metadata, duration, errors/retries and final
outcome. Synthetic models validate routing/security and do not establish actual
Groq reasoning quality. See the task's verification report for measured results.
