# Assets: persistent Resource Runtime module

The current module extends its static foundation with [ASSET-003 dynamic fields](assets-dynamic-fields.md).
That page documents the typed-slot API, stable JSONB storage, lifecycle and evolution rules.
The ASSET-002 scope and verification below remain historical foundation evidence.

ASSET-002 is a completed, validated implementation-specific slice, based on the ASSET-001 design
at erbas-contract commit `eb8227c`. The approved ASSET-002 scope deliberately
replaces the earlier proposed dynamic-field slice with six static resources.
It depends on RESOURCE-CONFLICT-001 (`002d890`), which adds explicit generic
domain conflicts. It makes no shared OpenAPI/Bruno, release, Java adoption or
frontend claim. Verification results are recorded below when complete.

## Composition and boundaries

`Alxarafe.Modules.Assets.Module` owns resource definitions, business rules,
repositories, action handlers, EF rows, schema/migrations and seed. Host references
only `AssetsModule` for the existing built-in assembly composition. No Asset
concepts or exceptions were added to Core or Resource Runtime in ASSET-002.

```text
ResourceEndpoints → ResourceRuntime → module IResourceRepository → AssetsDbContext
ResourceActionEndpoints → ResourceActionRuntime → module IResourceActionHandler
```

There are no Assets CRUD routes/controllers or `IHttpModule` implementation.
The five catalogue adapters share their identical stored fields and operations
through a narrowly scoped module repository; Asset mapping and transitions are
explicit. This first slice uses one entry assembly rather than creating empty
Domain/Application/Http projects. Rule code is in `AssetRules`; query translation
and database handling stay in the module adapters. This is not a new generic
persistence framework or Core extension.

## Resources and HTTP entry points

| Resource ID | Purpose / write permission |
| --- | --- |
| `assets.asset` | Concrete asset / `assets.maintain` |
| `assets.asset-type` | Persisted, administrable type / `assets.schema.manage` |
| `assets.manufacturer` | Manufacturer catalogue / `assets.maintain` |
| `assets.asset-model` | Model with required immutable manufacturer / `assets.maintain` |
| `assets.location` | Location catalogue / `assets.maintain` |
| `assets.asset-status` | Status catalogue with protected system codes / `assets.schema.manage` |

Use generic `POST /api/resources/{resource}`, collection/item GET, item PATCH and
item DELETE. CREATE returns 201 and Location, reads/PATCH return 200, successful
DELETE returns 204. PUT is not exposed. Discovery uses `/api/meta/resources` and
`/api/meta/resources/{resource}`. Existing metadata discovery remains available
to authenticated callers with per-operation/action authorization flags; descriptors
do not grant access to stored data. Resource API OpenAPI is derived automatically
by existing transformers; this is distinct from the shared specification.

Minimal asset input is `{ "name": "Maintenance laptop", "assetTypeId": "<uuid>" }`.
Discover an active type's UUID from its catalogue. The server immediately persists
and returns an asset with `DRAFT` lifecycle and the `IN_STOCK` status UUID.
`assetTypeId` is required and create-only. An immutable-field PATCH, including
an identical supplied value, returns 409 before persistence. ID, timestamps,
`lifecycle` and `statusId` are server-owned; supplying them is a 400 input error.

Asset fields in this slice: name, type, optional assetTag/serialNumber,
manufacturer/model/location, status, lifecycle, purchaseDate/purchasePrice,
condition, notes and server timestamps. Optional fields serialize as null and
may be cleared through PATCH. Omission preserves them. `condition` is UInt8
with the additional inclusive 0..100 constraint. The runtime enforces intrinsic
and declared bounds without a parallel type validator in an HTTP endpoint.

Strings use the runtime's Unicode scalar validation, including rejection of NUL
and unpaired surrogates. Names must contain non-whitespace text and have at most
255 scalars; codes are nonblank, at most 30 scalars, immutable and exactly
case-sensitive. No implicit trimming/case folding occurs. Asset tags, when
present, are nonempty, at most 255 scalars and exactly unique; multiple null
tags are allowed. PostgreSQL text equality/order uses ordinal C collation.
AssetType additionally has an optional description. Master rows have active
state and timestamps; model codes are unique within a manufacturer.

Purchase dates use ISO calendar dates; timestamps represent UTC instants and
round trip at PostgreSQL microsecond precision. Purchase price is nonnegative
Decimal with precision 18 and scale 2; input outside that declared representation
is rejected before persistence, not rounded implicitly. No currency subsystem is
introduced. Both create/update representations come from the persisted module
rows and can be recovered after a Host restart.

## Lifecycle, actions and integrity

Execute an entity action using
`POST /api/resource-actions/assets.asset/{action}/execute` with
`{ "target": { "id": "<uuid>" }, "parameters": {} }`.

| Action | Preconditions / result |
| --- | --- |
| `complete` | DRAFT only; validate the full static representation using the existing ResourceValueValidator and validate references/coherence, then transition to READY. Status remains IN_STOCK. |
| `retire` | READY, not operationally ASSIGNED; transition atomically to RETIRED lifecycle and RETIRED status. Requires sensitive-action authorization. |

Neither action accepts arbitrary fields, lifecycle/status/type overrides or extra
arguments. Invalid transitions return 409; absent targets/actions return 404.
There is no reactivate. Retired assets remain readable and cannot be patched,
deleted or completed/retired again. No direct status/lifecycle write bypass exists.
Unassigned DRAFT/READY assets without dynamic values may be deleted; repeated
deletion returns 404. ASSET-003 protects referenced values from parent deletion.

Assignments/people are not implemented in this six-resource slice. There is no
public assignment field/action; API-created assets are unassigned. The retirement
guard also rejects a legacy/internal ASSIGNED status, but this is not a claim
that Person/assignedToId integrity has been implemented. Assignment actions and
Person persistence belong to a later approved task.

Type must exist and be active on creation; deactivating a type preserves existing
assets and does not prevent their editing/completion. New optional selections
require existing active targets; unchanged inactive references remain readable.
A selected model requires an explicitly supplied matching manufacturer. PATCH
validates the resulting aggregate, so clearing/changing a manufacturer while
retaining an incompatible model conflicts. A model's manufacturer is create-only.

Deleting referenced types, manufacturers, models, locations or statuses conflicts;
manufacturer references from models are protected too. Foreign keys use RESTRICT,
not destructive cascades. Reserved statuses ACTIVE, IN_STOCK, ASSIGNED, IN_REPAIR
and RETIRED cannot be deleted, deactivated or recoded; labels may be edited.
Custom statuses support catalogue CRUD but cannot be selected directly on an asset.

All module mutations take a PostgreSQL transaction advisory lock in that tenant's
business database before loading/validating state. Reads do not take that lock.
This intentionally serializes Assets writes within one tenant for this slice;
it is a throughput limitation, not a proposed general runtime concurrency model.
Different tenant databases are independent. Database FKs/unique indexes provide
additional integrity protection. Known provider integrity failures are translated
inside this adapter to explicit ResourceConflictException; runtime never examines
SQL/EF messages. Failed operations roll back and preserve data/timestamps.

## Permissions and errors

All data reads require `assets.read`. Every mutation requires read plus its
resource write permission. `complete` requires read+maintain; `retire` requires
read+maintain+`assets.actions.sensitive`. Schema administration grants no asset
maintenance, and sensitive permission alone grants no normal maintenance.
Existing CORE administrators receive these module policies through the existing
admin shortcut; no identity/bootstrap-role system was added. Administrator status
never bypasses lifecycle, integrity or immutability rules.

The existing authoritative user, membership, tenant and module gates apply.
Revocation is checked through the existing authorization adapter; client claims
or tenant headers cannot select a database. Module disablement returns 503 and
reenabling preserves data. Domain conflicts use 409 `resource_conflict` and a
stable reason in the common errors array; action conflicts retain the existing
409 `resource_action_conflict` envelope. Malformed JSON, wrong types, constraints,
blank names, unknown fields and protected-field input remain 400. Missing
resources/references are 404; authentication/permission failures are 401/403.
Unexpected failures remain safely classified; no database text is returned.

## Persistence, seed and queries

The module uses the same centrally resolved independent business database for
each tenant as other business modules, with the six static `assets_*` tables plus ASSET-003's two dynamic-field tables and separate
`__AssetsMigrationsHistory`. It opens registered active tenant databases before
migration through ITenantDatabaseMigrationRunner. It never creates a production
database or falls back to a global Assets connection. No tenant discriminator
column, new tenant-selection source or module-owned secret configuration exists.

Startup seed is repeatable and preserves edits. It adds LAPTOP, Dell,
Latitude 5450, Main office and the five reserved statuses with stable references.
No AssetType is compiled as a resource definition: new types are ordinary data
and require no resource registration, migration or schema change. The generic
AssetType resource definition describes the catalogue itself. This seed is
intentionally smaller than the full ASSET-001 future dataset.

Filters use the existing Resource API `filter` JSON query parameter, for example
`{"field":"assetTypeId","operator":"eq","value":"<uuid>"}`. Repeated filters
combine with AND before pagination/totals. Type, manufacturer, model, location,
status and lifecycle are filterable; catalogue code/name/active and IDs support
their metadata-declared operators. Sorting supports the declared ID/name/code/
createdAt fields and appends ID as a stable tie breaker. Existing offset/limit
validation applies. Unsupported filters/search fail explicitly; no joins,
expansion or fabricated search capability are advertised. Relation metadata
contains stable target resources/IDs, leaving later select/search UI decisions open.

## Evidence and next tasks

AssetsTests exercise real production composition, metadata, PostgreSQL migration
and constraints, persistent runtime CRUD, HTTP, permissions, tenant isolation,
Host reinitialization, rollback and concurrent duplicate/transition/reference
operations. Native fixture writes used to arrange legacy-invalid/assigned state
and cleanup are test-only; production paths use Resource Runtime adapters.
The existing suites are retained, with composition expectations updated for the
new registered module and explicit fixture repositories selected by identity.

Verification completed on 2026-10-26:

- Docker build/Release publish and solution build passed with zero warnings/errors.
- Final focused production-slice/composition tests passed (45 HTTP/PostgreSQL
  tests and 5 Host boundary tests), including a replay of the initial migration
  after removing trailing whitespace without changing its effective SQL.
- `dotnet format whitespace` ran in Docker on the new module/test files.
- Authoritative `./bin/check`, with `ERBAS_CONTRACT_DIR` pointing to a clean
  isolated published v0.12.0 checkout, passed all 977 native tests: 176 architecture,
  548 Host integration (including 38 Assets cases), 150 metadata and 103 Runtime.
- Shared conformance, local Bruno suites, isolated Host startup/OpenAPI, fresh
  installation, persisted PostgreSQL state, log secrecy and validation
  database/container cleanup all passed. No shared collection was changed.
- Diff, documentation links/index, meaningful literals and credential-marker
  review passed. No lockfile or additional Core/Runtime change is included.

Implementation findings were module-owned: the initial EF snapshot needed
explicit foreign-key indexes to match provider conventions, and the migration
DDL was aligned with the provider's exact key/index names. Model comparison and
the native migration test now report no pending differences. The HTTP fixture's
double consumption of response streams was fixed before final validation. No
failed assertion, warning or runtime exception was suppressed to obtain a pass.
The earlier generic conflict limitation was resolved independently in
RESOURCE-CONFLICT-001; no further generic runtime mechanism was needed.

[ASSET-003](assets-dynamic-fields.md) implements dynamic definitions/values and
their explicit lifecycle, evolution and historical-value policy.
ASSET-004 adds people/assignments and further business actions/integrity.
ASSET-005 broadens seed and coverage. Neither ASSET-004 nor ASSET-005 has started. A later
cross-stack review decides whether behavior belongs in the shared contract;
Core extraction requires separate evidence and approval. Frontend, full ITAM,
generic relation expansion and automatic select/search with
global/tenant/user settings remain out of scope.
