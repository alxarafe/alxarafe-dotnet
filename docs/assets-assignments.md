# Asset assignments and custody history (ASSET-004)

Assignment history is canonical. Current assignment is derived exclusively from
the unique `AssetAssignment` whose `endedAt` is absent. No assignment cache or
mutable `assignedTo` field exists on Asset. Dynamic fields describe properties;
assignments describe custody. ASSET-003 semantics remain unchanged.

## Model and references

`assets.asset-assignment` exposes ID, authoritative tenant ID, asset ID,
target type, target ID, derived target resource ID, started/ended timestamps,
start/end reasons, notes and created/updated timestamps. All fields are read-only.
The initial target type is `LOCATION`, referencing `assets.location` with a real
restrictive foreign key. This uses an existing stable resource rather than
inventing person/user identities. Other target types remain future work and will
need explicit validation, reference integrity and metadata.

Asset `locationId` remains its physical/base location. A LOCATION assignment is
explicit custody at a location; it does not change that base location. Existing
operational `statusId` is also unchanged and is not a current-assignment flag.
Consumers must query assignment history to determine current custody.

Tenant isolation follows existing database-per-tenant resolution. API tenant ID
is projected from the authoritative request context; no discriminator column or
client-supplied tenant exists. Asset and target foreign keys resolve only inside
that database. A foreign tenant's identifiers are not found.

## Actions and lifecycle

Existing Resource action endpoints on `assets.asset` provide:

| Action | Input | Effect |
| --- | --- | --- |
| `assign` | `targetType`, `targetId`, optional `reason`, optional `notes` | Create current assignment; conflict if already assigned |
| `unassign` | required `reason` | Close current assignment; conflict if absent |
| `transfer` | `targetType`, `targetId`, required `reason`, optional `notes` | Atomically close current and create successor; conflict if absent or same target |

Only READY Assets permit custody actions. DRAFT and RETIRED return 409. New
assignments/transfers also require operational status ACTIVE or IN_STOCK and an
active target. Unassignment remains possible when the target is inactive.
Retirement requires explicit unassignment first: an open assignment causes 409.
Historical records remain readable after retirement or target deactivation.
Deleting any Asset or Location with assignment history is rejected, regardless
of whether the assignment is closed. No business history is cascade-deleted.

Reasons, when present, must contain non-whitespace text, exclude NUL and fit
255 characters. Closing operations require a reason. Notes allow up to 4,000
characters and exclude NUL. Invalid input returns 400; valid requests conflicting
with lifecycle/current custody return 409. Missing asset/target returns 404.

Timestamps are server-generated UTC at PostgreSQL microsecond precision. There
is no backdating input. Periods are half-open `[startedAt, endedAt)`, allowing
adjacent transfer periods and equal start/end timestamps. End must not precede
start. A clock earlier than recorded current start or historical end causes 409;
the service does not silently rewrite chronology.

## Runtime, permissions and API

History uses generic ResourceEndpoints GET/list, filtering and metadata. Direct
create/update/delete are undeclared and return 405, including attempts to reopen,
rewrite or move historical rows. Every restriction is **B: Assets responsibility**:
custody rules, append/close history, immutable ownership and historical deletion
protection are business semantics. Existing operation declarations, repositories,
action handlers, scalar validation and conflict handling express them without
new endpoints. No **A: generic Runtime deficiency** was found.

`assets.read` permits history access. Custody actions require both `assets.read`
and the independent `assets.assignments.manage` permission. Ordinary
`assets.maintain`, `assets.schema.manage` and sensitive lifecycle permissions do
not imply assignment rights. Assignment rights do not grant ordinary maintenance
or retirement. Existing sensitive lifecycle permissions remain unchanged.

For example, POST to
`/api/resource-actions/assets.asset/assign/execute` with
`{"target":{"id":"<asset-uuid>"},"parameters":{"targetType":"LOCATION","targetId":"<location-uuid>","reason":"Initial custody"}}`
returns 204 on success. Discover definitions through existing metadata endpoints.
List `/api/resources/assets.asset-assignment` using the existing `filter`
encoding for `assetId eq <assetId>`; add `endedAt eq null` for current custody.
Sort by `startedAt` for history. Target metadata includes its resource relation;
temporal filters use canonical Runtime DATETIME parsing. No UI is included.

## Persistence, transactions and concurrency

The module migration adds one stable `assets_assignments` table. Existing Asset
and dynamic-value storage are unchanged. Per-tenant storage makes an additional
tenant index/discriminator unnecessary. Indexes cover asset/current lookup,
asset-start-ID chronology and target references. PostgreSQL enforces:

- Partial unique asset index where end is NULL: at most one current assignment.
- GiST exclusion on asset equality and timestamp-range overlap: no overlapping
  periods, including open periods.
- Finite timestamps, end >= start, closed-row reason and supported target checks.
- Restrictive foreign keys to Asset and Location.

Actions reuse the existing tenant transaction advisory write lock. Transfers
flush the old closure before inserting the successor inside that same transaction,
so immediate uniqueness/exclusion checks hold at each statement. The transaction
commits both changes and Asset audit timestamp together; insertion failure rolls
everything back. Readers cannot observe two current assignments. Independent
concurrent assignment requests serialize, and database constraints provide the
final integrity boundary even for writers outside that application lock.
SQL exclusion violations map through the module's existing explicit conflict
adapter; HTTP never depends on parsing exception messages.

The migration installs PostgreSQL `btree_gist` in `public` and qualifies the UUID
operator class. Deployment credentials must have extension installation rights,
or administrators must preinstall it in that schema. It is available in the
repository's PostgreSQL image. Rollback refuses when assignment history exists;
an empty-table rollback removes the table but never the potentially shared
extension. EF model/snapshot and incremental migration are kept aligned.

## Limits and later work

**C: intentionally out of scope** includes additional target kinds, personal
custodians, caller backdating, manual history corrections, historical imports,
bulk operations, actor identity auditing beyond existing timestamp conventions,
UI, reservations and a general temporal framework. This slice does not introduce
event sourcing, Core extraction or relation-editor policy. Business history and
custody policies remain inside Assets until actual use demonstrates a reusable
platform capability. Ordinary API users cannot rewrite historical timestamps;
privileged direct SQL remains an administrative trust boundary.

## Verification

Validation completed successfully:

- Focused Docker verification: 101 HTTP/PostgreSQL/integration cases and five
  Host boundary checks, with no skips. The initial metadata assertion was
  corrected to the existing `declared` contract; the added rollback probe's
  entity name was corrected before successful verification.
- Authoritative `ERBAS_CONTRACT_DIR=/tmp/erbas-contract-asset-validation-v0.12.0
  ./bin/check`: 1,033 native tests passed (176 architecture, 604 Host integration,
  150 resource metadata, 103 Runtime), zero skips, zero build warnings/errors.
  The isolated contract checkout was clean and pinned to published v0.12.0.
- Docker image/Release publish, isolated Host startup, shared conformance,
  local Bruno suites, PostgreSQL persistence checks, sensitive-log checks and
  disposable database/container cleanup passed.
- New coverage exercises current/history discovery through generic Runtime,
  permission separation, tenant references, invalid input 400 versus state
  conflict 409, immutable history, disabled targets, retirement, temporal/FK
  constraints, concurrent assigns/transfers and insertion-failure rollback.
  Existing dynamic-field behavior passed unchanged. Populated static upgrades
  preserve data; destructive migration rollback with assignment history is
  rejected and the rows remain readable. EF reports no pending model changes.
- Docker `dotnet format whitespace`, diff/scope review, UTF-8/NUL and credential
  marker review, and relative documentation link checks passed. Only module,
  integration-test and documentation files changed. No Core/Runtime, shared
  contract/Bruno, dependency lockfile or frontend changes were introduced.
