# ASSET-003: dynamic Asset fields

This implementation-specific slice extends [ASSET-002](assets.md). It remains
inside Assets: the shared contract, Bruno collection, Core and Resource Runtime
sources are unchanged. ASSET-003 approval selected a fixed typed-slot projection
for the first five canonical types; it does not promote a dynamic schema framework.

## Existing semantics reused and the representation boundary

The pre-implementation review found reusable public `ScalarType`, `ScalarValue`,
`FieldDefinition`, `TextConstraints`, `NumericConstraints`, `DecimalConstraints`,
`ResourceValueValidator`, `ResourceRegistry` definition validation, and
`ResourceTransport.Parse`/`Project`. Assets uses these existing implementations
for primitive types, intrinsic ranges, exact numbers, constraint evaluation and
canonical JSON/date representation. It defines no alternative type enum or
constraint evaluator. Definition configuration is checked by building an isolated
semantic ResourceRegistry around its data-derived FieldDefinition. This exercises
the existing validation seam without altering the application's frozen registry
or registering a global resource for each tenant field.

**Resource Runtime dispone de semántica dinámica reutilizable para valores
escalares, pero actualmente no permite declarar en un ResourceDefinition una
propiedad cuyo tipo dependa en runtime de otra definición persistida.**

In English: Runtime has reusable dynamic scalar semantics, but cannot currently
declare a resource property whose type depends on another persisted definition
at execution time. The approved five-slot shape adapts representation only.
This is category **B**, an Assets adapter responsibility for this experiment.
A polymorphic single `value` property would be a potential generic extension,
deliberately **C/out of scope** here. No Core/Runtime patch is needed or made.

## API and discoverable metadata

Two resources use the existing generic ResourceEndpoints and tenant-aware Runtime:

| Resource | Fields |
| --- | --- |
| `assets.asset-field-definition` | id, assetTypeId, code, name, description, fieldType, required, active, displayOrder, typed configuration, derived valueField, timestamps. |
| `assets.asset-field-value` | id, assetId, fieldDefinitionId, derived assetTypeId, exactly one typed slot, timestamps. |

Both support POST, collection/item GET, PATCH and DELETE. No Assets CRUD endpoint
or new action dispatcher exists. IDs are opaque server UUIDs; type ownership,
definition code and fieldType are create-only. Metadata discovery describes the
fixed system resource schema. Persisted field definitions describe the tenant's
business schema as data, and are not injected into global resource metadata.

| Canonical ScalarType | Slot | Configuration |
| --- | --- | --- |
| STRING | stringValue | Optional minimumLength/maximumLength using TextConstraints. |
| INT32 | int32Value | Intrinsic Int32 range plus optional minimum/maximum NumericConstraints. |
| DECIMAL | decimalValue | DecimalConstraints precision/scale plus optional minimum/maximum range. |
| BOOLEAN | booleanValue | Native Boolean scalar, without text coercion. |
| DATE | dateValue | Native Date scalar with the Runtime's canonical ISO calendar representation. |

`fieldType` uses these exact uppercase names from the platform ScalarType enum.
The derived read-only `valueField` tells a client which slot to submit/render.
Each value row supplies exactly one slot of precisely that type. Other slots are
omitted, not null placeholders. Zero and false are present valid values; absence
means no AssetFieldValue row. No slot accepts null, including for optional fields.
DELETE explicitly removes an eligible current value. A required STRING may be
empty only if its configured length constraints allow it; required means presence
and semantic validity, not an implicit nonblank-text constraint.

For example, create a definition through `/api/resources/assets.asset-field-definition`:

```json
{"assetTypeId":"<uuid>","code":"seats","name":"Seats","fieldType":"INT32","required":true,"minimum":1,"maximum":9}
```

Then POST `/api/resources/assets.asset-field-value`:

```json
{"assetId":"<uuid>","fieldDefinitionId":"<uuid>","int32Value":3}
```

PATCH that value resource with `{ "int32Value": 4 }`. Supplying stringValue,
multiple slots, no slot, null or an incompatible primitive/constraint is 400.
Both type-level transport validation and the data-derived FieldDefinition use
the shared platform semantics; the adapter adds ownership/state/one-slot rules.

Generic collection filters on assetTypeId retrieve applicable definitions;
displayOrder sorting plus the ID tie breaker gives deterministic ordering.
Filters on assetId retrieve current values, including inactive historical ones.
Combine the two by fieldDefinitionId. Name/description provide label/help data;
required/active/order/type/configuration and valueField are available for a future
generic UI. No type-specific Vehicle/Laptop rendering rule, joins, expansion,
relation field/editor policy or new UI is provided.

DECIMAL defaults to precision 38 and scale 18 on definition creation; both are
discoverable and explicitly configurable. Precision must be positive, scale
must fit precision, and both are bounded by Runtime's existing numeric transport
limit. These bounds fit PostgreSQL JSONB numeric storage. No decimal is converted
through double or CLR decimal. Numeric configuration is stored as exact
ResourceNumber canonical text; the public API accepts/returns JSON numbers.
Changing scale never rounds values to make them fit. DATE parse/project is reused
directly, with no Assets date convention. Constraint fields incompatible with
the selected type, impossible bounds or unsupported types are 400. No defaults
for actual field values are implemented or silently filled on completion.

## Persistence and integrity

The module adds two tables under its existing tenant migration history:
`assets_field_definitions` and `assets_field_values`. The latter has one JSONB
`value` column containing the canonical native JSON scalar, not an unchecked
string wrapper. There are no string/integer/decimal/Boolean/date storage columns
and no columns added to Asset for user-defined fields. The five slots exist
only in the fixed resource projection. Administrators change rows, not DDL.

Each value has a unique `(assetId, fieldDefinitionId)` pair. A server-derived
assetTypeId participates in two composite foreign keys, one to the Asset and
one to the definition; PostgreSQL therefore enforces matching type ownership
even outside Runtime. JSON null is also rejected by a database check. All
references use RESTRICT. Values/definitions are tenant business data resolved
by the existing authoritative database-per-tenant mechanism, with no global
store, tenant selector or tenant discriminator column.

Mutation adapters reuse the existing per-tenant transaction advisory lock.
This serializes definition evolution, value writes, Asset changes, completion
and retirement so a pre-check cannot race an incompatible commit. Definition edits
scan existing values in the module while holding that lock; large catalogues
can therefore make schema administration expensive. This is an explicit first-slice
scalability limit, not a new generic migration/validation infrastructure. Value changes
also update the parent Asset timestamp atomically. Metadata-only definition
edits do not rewrite values or Asset timestamps. Each CRUD request is atomic;
there is no multi-value batch/aggregate replacement operation in this slice.

## Lifecycle and evolution policy

Minimal `name` + `assetTypeId` creation still persists DRAFT immediately, with
zero dynamic values. Missing required fields are valid in DRAFT. `complete`
checks every active required definition for presence and shared semantic validity
before allowing READY; zero/false are not missing. No default/backfill is applied.
READY mutations preserve complete active requirements. Required value DELETE
on READY conflicts; DRAFT can explicitly remove it and remain incomplete.
RETIRE is irreversible. RETIRED blocks all value creation, update and deletion;
reads remain available. Retirement also rechecks active completeness.

| Definition operation | Policy |
| --- | --- |
| Add optional | Allowed; existing Assets need no value and remain valid. |
| Add/promote/activate active required | Reject 409 if any READY or RETIRED Asset of that type would lack a valid value. DRAFT may remain incomplete. |
| Tighten constraints | Validate every stored value for that definition, including DRAFT, inactive and RETIRED history; reject 409 if any would become invalid. |
| Change fieldType, code or owning type | Create-only identity; reject 409 even without values. No implicit conversions or reparenting. |
| Disable | Preserve all values, leave them readable and reject normal value create/update/delete while inactive. |
| Change an inactive definition | Still cannot invalidate its stored values. Requiredness has no active completeness effect until activation. |
| Delete definition | Only unused optional definitions; values or required flag block deletion. Deactivate or explicitly relax instead. |
| Delete referenced type/Asset | Reject while definitions/values reference it; no cascade that erases history. |

The explicit ASSET-003 active-only completeness rule supersedes ASSET-001's
earlier inactive-required completion proposal: disabling stops applicability,
not persistence. Reactivation must revalidate all completed/retired Assets.
To introduce a new required field without a migration framework, add it as
optional, explicitly populate editable Assets, then promote it. A retired Asset
with no value blocks promotion/activation; normal maintenance cannot backfill it.
No hidden grandfathering, automatic DRAFT downgrade or destructive conversion exists.

## Permissions, errors and limits

Definitions require read+`assets.schema.manage` for writes. Values require
read+`assets.maintain`; schema administration alone cannot maintain values.
Retirement retains read+maintain+sensitive authorization. Existing authoritative
membership, module gating and persisted permission checks remain in force.
All data reads require `assets.read`; metadata descriptors retain the existing
authenticated discovery/authorization-flag behavior.

400 means invalid shape/type/constraint/configuration; 404 means absent target or
reference, including identifiers from another tenant database. 409 means valid
input conflicts with ownership, uniqueness, lifecycle, inactivity, requiredness
or safe definition evolution. The existing common error envelopes and generic
conflict mechanism are reused. Parser/SQL/credential text is not returned.
NUL cannot be stored in PostgreSQL text/JSONB and is explicitly rejected as input.

Normal APIs cannot persist invalid scalar values. Legacy/privileged database
corruption is rejected by reads rather than serialized as valid data, and it
blocks completion. Existing Runtime validates current repository output before
PATCH; repairing a corrupt DRAFT value can require explicit DELETE/recreation.
Repair/migration of READY/RETIRED corruption is not provided as a bypass route.
Inactive history here means preserving current stored values, not a full temporal
audit/version log of every earlier edit.

Later tasks may add more canonical types/constraints, references, batched values,
annotation transport and richer diagnostics after evidence and approval. Assignments are now implemented separately in [ASSET-004](assets-assignments.md);
further business actions remain future work. Procurement, depreciation,
inventory discovery, maintenance, attachments, UI, global/tenant/user editor
selection and a general schema migration framework remain excluded.

## Verification

Verification completed on 2026-10-26:

- Solution build and Docker Release publish passed with zero warnings/errors.
- Final focused verification passed 80 HTTP/PostgreSQL cases plus 5 Host
  boundary checks, including the corrected populated-schema migration test and
  a post-review replay after representing NUL checks with char.MinValue.
- Authoritative `./bin/check`, using `ERBAS_CONTRACT_DIR` for an isolated clean
  published v0.12.0 checkout, passed 1,012 native tests: 176 architecture,
  583 Host integration (35 new dynamic-field/migration cases), 150 metadata
  and 103 Runtime. Existing Assets tests passed alongside the new cases.
- Shared conformance, local Bruno suites, isolated Host startup/OpenAPI,
  persisted PostgreSQL state, log secrecy and database/container cleanup passed.
- Tests prove exact decimal/date round trips, explicit absence/zero/false,
  configuration/input 400 versus state-conflict 409, active/inactive evolution,
  retired/tenant protections, immutable ownership and database constraints.
  Physical schema inspection before/after administrator changes stayed equal.
  Upgrade/rollback/re-upgrade preserved populated ASSET-002 static data.
- EF snapshot/model comparison reported zero differences. The migration test
  initially used the repository's migration ID as an EF target; it was corrected
  to the existing IMigrationsIdGenerator naming pattern, not suppressed.
- Docker `dotnet format whitespace`, diff/credential-marker review and relative
  documentation link checks passed. No shared semantic/Runtime/Core source,
  shared contract, shared Bruno collection or lockfile was changed.

The ASSET-003 verification above is historical. [ASSET-005](assets-queries.md)
now supports typed value-content filtering on the existing resource.
Pattern/allowed-set/date-bound configuration and bulk value replacement remain
future scope. No premature Core extraction is justified:
the module still supplies lifecycle,
ownership, history and evolution policies. A later review can assess whether
the observed pattern warrants a generic polymorphic representation or dynamic
definition facility; ASSET-003 does not anticipate that design.
