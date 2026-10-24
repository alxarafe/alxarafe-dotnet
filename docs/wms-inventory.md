# WMS inventory foundation (WMS-004)

## Scope and contract classification

These are public HTTP **implementation-specific .NET extensions**, under the
[shared extension policy](https://github.com/alxarafe/erbas-contract/blob/main/docs/common/implementation-extensions.md).
They are not contractual ERBAS APIs. Portable clients must not depend on them.
The external scenario loader explicitly targets this .NET extension.
`contract.revision` remains published immutable `v0.11.1`; shared OpenAPI,
shared Bruno, tags, Java and existing HTTP behavior are unchanged.

Promoting this surface requires an approved cross-stack observable-behavior
review, shared OpenAPI and conformance coverage, and an additive MINOR release.
Java can implement the same primitives with its own domain/application layers,
JPA/PostgreSQL transactions, unique keys, FKs, ledger aggregation and migrations;
EF Core is not a client-visible requirement. Review idempotency, numeric/time
precision, permissions, errors and paging before promotion. No simultaneous Java
implementation is required for an explicitly implementation-specific extension;
this work does not claim Java support or cross-stack conformance for WMS-004.
Existing shared v0.11.1 conformance remains mandatory.

## Ownership and domain

Source inspection found no product/item/reference owner in another module.
WMS therefore owns a minimal stockable `StockItem`, reusing its existing UoM
and optional ItemFamily. This does not introduce a sales/product master or a
cross-module dependency on `WmsDbContext`.

- `Warehouse`: server UUID, globally unique stable code, name.
- `Location`: server UUID, warehouse UUID, code unique within the warehouse,
  enabled flag. Codes such as `A.01.01` need no separate aisle hierarchy.
- `StockItem`: server UUID, globally unique SKU, description, base UoM UUID,
  optional ItemFamily UUID. No implicit conversions between units.
- `InventoryMovement`: server UUID, warehouse/item/location UUIDs, business
  `occurredAt`, signed `quantityDelta`, base UoM UUID, type and external key.
- `InventoryBalance`: current sum grouped by warehouse/item/location/base UoM.
  It is a query projection, not a writable or separately materialized entity.
  Zero positions are omitted. A single PostgreSQL snapshot supplies each page
  and its total, so concurrent committed movements cannot lose balance updates.

Codes, SKUs and external keys are case-sensitive ASCII, 1–64 characters,
`[A-Z0-9][A-Z0-9_.-]*`, without normalization. Names/descriptions use existing
WMS label normalization (trim boundary space/tab/CR/LF, 1–255 Unicode scalars).
Quantities are exact decimal `numeric(20,6)`: nonzero deltas, at most six decimal
places, absolute maximum `99999999999999.999999`. Excess precision is rejected,
not rounded. `RECEIPT` is positive, `ISSUE` negative, `ADJUSTMENT` either sign.
Negative balances are permitted: this foundation records inventory facts and
does not implement reservations or an availability/stock-issuance workflow.

Timestamps require an explicit offset and microsecond precision and are stored
as UTC instants. They express business occurrence, not arrival order. Backdated
facts are allowed. Movements have no update/delete endpoints; corrections are
new signed movements. Disabled locations reject new facts; exact historical
retries remain valid. Masters have no update/delete endpoints in this phase.

`(warehouseId, externalKey)` is the persistent idempotency identity. First write
returns 201; an exact retry returns 200 with the original UUID, including under
concurrent requests. Reusing a key with a different fact returns 409. Timestamp
offsets representing the same instant compare equal. A location/warehouse
composite FK and item/base-UoM composite FK prevent inconsistent facts. All
master FKs restrict deletion. No demo data is part of the product or migration.

## HTTP surface

All paths are relative to a caller-supplied base URL.

| Resource | Methods | Exact collection filters | READ / WRITE permissions |
| --- | --- | --- | --- |
| `/api/wms/warehouses` | POST, GET, GET `/{id}` | `code` | `wms.warehouses.read` / `wms.warehouses.write` |
| `/api/wms/locations` | POST, GET, GET `/{id}` | `warehouseId`, `code` | `wms.locations.read` / `wms.locations.write` |
| `/api/wms/items` | POST, GET, GET `/{id}` | `sku` | `wms.items.read` / `wms.items.write` |
| `/api/wms/movements` | POST, GET, GET `/{id}` | `warehouseId`, `itemId`, `sku`, `locationId`, `from`, `to` | `wms.movements.read` / `wms.movements.write` |
| `/api/wms/inventory` | GET | `warehouseId`, `itemId`, `sku`, `locationId` | `wms.inventory.read` |

POST warehouse: `{ "code": "WH-01", "name": "Warehouse" }`.
POST location: `{ "warehouseId": "<uuid>", "code": "A.01.01", "enabled": true }`.
POST item: `{ "sku": "ITEM-01", "description": "Item", "baseUomId": "<uuid>", "itemFamilyId": null }`.
`itemFamilyId` is optional. POST movement:

```json
{
  "warehouseId": "<uuid>",
  "itemId": "<uuid>",
  "locationId": "<uuid>",
  "occurredAt": "2026-06-20T09:00:00+02:00",
  "quantityDelta": 1.25,
  "uomId": "<uuid>",
  "type": "RECEIPT",
  "externalKey": "IMPORT-0001"
}
```

Bodies reject missing required, duplicate, wrong-case and unknown properties.
POST requests reject query parameters. Collections use existing COLLECTIONS-001
`offset`, `limit`, `items`, `total`, `order` envelopes and defaults/limits.
Filters combine with AND. Time intervals are `[from, to)`; supplied bounds must
be valid offset timestamps and `from < to`. UUIDs use canonical `D` syntax.
Natural resolution uses exact filtered collections; UUID resolution uses GET by ID.
Ordering is warehouse code; location warehouse UUID/code; item SKU; movement
occurrence/UUID; inventory warehouse/item/location UUIDs, all ascending.

Errors preserve shared closed `code`/`errors` envelopes and no-store behavior:
401 `unauthorized`, 403 `forbidden`, disabled-module 503 `module_disabled`,
400 `invalid_request` or `wms_inventory_reference_not_found`,
409 `wms_inventory_conflict` or `wms_location_disabled`,
404 `wms_inventory_not_found` for an unknown UUID. Technical codes are stable
across locales; these errors contain no new user-facing translated text.

Authentication uses normal Identity bearer login and effective current user
state. Permissions are independent: WRITE does not grant READ. As with existing
module administration policy, a current CORE administrator can use these APIs;
non-admin users need the corresponding permission claim. This creates no
special seed identity or authentication bypass. Existing UoM/family creation
still requires the existing CORE administrator authorization; the loader uses
an existing normally authorized account. There is no new claim-management API.

## Development identities

`admin` means the normally configured CORE administrator; `user` means the
optional WMS development reader. These are documentation labels, not username
checks in authorization. Emails are configurable. The default reader email is
`user@example.test`; provisioning requires an explicit `ERBAS_WMS_USER_PASSWORD`.
`ERBAS_WMS_USER_EMAIL` optionally overrides the email. Compose exposes the pair
as `Wms:DevelopmentUser:Email`/`Password` to the development `app` service.

`WmsModule` contributes the reader's four READ permission claims through existing
`DevelopmentUsersOptions`; generic `SecuritySeed` persists the Identity account
and claims. The profile applies only in Development and is absent by default.
Repeated provisioning does not reset an existing password or duplicate claims.
It grants no administrator, movement or WRITE rights. Existing manually assigned
rights are not revoked: to use this restricted profile, supply an ordinary
account without additional privileged claims. Authorization always follows the
account's effective permissions and current administrator/enabled state.

| Operation | Configured admin | Restricted development user |
| --- | --- | --- |
| GET warehouses, locations, items, inventory | 200 | 200 |
| GET movements | 200 | 403 |
| Valid WMS creation | 201 | 403 |
| Other WMS writes, including catalogue associations | Authorized | 403 |

The administrator's access comes from the existing CORE administrator claim.
The reader receives exactly `wms.warehouses.read`, `wms.locations.read`,
`wms.items.read`, `wms.inventory.read`. In Development the AI module also
explicitly contributes `ai.assistant.invoke`, without WMS privileges. Neither identity gains privileges from
its name, a demo flag or AI. Existing shared UoM/family/storage-attribute
permissions and AI capability definitions are unchanged.

## Persistence, validation and external loading

Migration `202610240001_InventoryFoundation` adds four WMS-owned tables, unique
natural/idempotency keys, composite FKs and filter indexes, while retaining the
separate WMS migration history. Existing migrations remain unchanged. Upgrade,
downgrade, persistence, concurrent idempotency, decimal sums, filtering,
authorization, module gating and module boundaries have native test coverage.

The independent `erbas-demo-data` directory runs `validate.py`, then authenticates
via `/api/auth/login`, creates/resolves catalogues and references, records facts,
and verifies the result against `stock.json` and `expected-ai-cases.json`.
Supply the URL explicitly and keep credentials in environment variables or use
the loader's hidden interactive password prompt. It never writes SQL or stores
tokens. An existing warehouse is verification-only: a complete load returns
`already_loaded`; a partial or divergent scenario fails without repair writes.
A failed initial multi-request load can leave partial data: there is no HTTP
transaction spanning the whole seed. Use a fresh disposable scenario environment
to retry a partial load; the loader never fabricates compensating movements.

This phase adds no lots, expiry, costing, reservations, logistics workflows,
analytics, frontend, AI tools or capability descriptors. AI READ composition is now implemented separately in the
[AI-API-001 candidate](ai-read-primitives.md); no analytic capability is added. Full `bin/check` verifies v0.11.1 conformance, not shared
conformance of these implementation-specific extensions.


## Verification on 2026-10-24

The implementation started from clean `main` at `b062f4a`, matching `origin/main`.
The shared contract checkout remained clean at published `v0.11.1` commit
`5517f0df5d3cacc57c35ef704131dcb30ad9dde2`, also matching its remote `main`.
No shared contract files or existing tags were modified.

- Focused WMS/domain/architecture run: 38 passed; focused WMS integration,
  composition, persistence and migration run: 101 passed.
- `dotnet format Alxarafe.sln --no-restore --verify-no-changes --include <task C# files>`
  inside the Compose `app` service: exit 0.
- `ERBAS_CONTRACT_DIR=<clean published v0.11.1 checkout> ./bin/check`: exit 0;
  Docker image build, restore/build (zero warnings/errors), 128 architecture tests
  and 288 integration tests passed, no skipped tests. Published shared conformance,
  local platform Bruno (3 requests, 1 test, 5 assertions), PostgreSQL persistence,
  migration history, log secrecy and validation cleanup all passed.
- External `seed.sh <supplied network URL>` against an isolated real Host and
  PostgreSQL: `loaded`, then `already_loaded`. Both runs passed complete
  `stock.json` comparison and `expected-ai-cases.json` composition checks:
  1 warehouse, 15 locations, 15 references, 40 movements, 20 stock positions.
- API evidence: `AGUA-X-1L` has 480 UN in A.01.01 and 120 UN in B.01.04,
  total 600 UN; C.01.05 has 300 UN of CONS-TOM-400G and 20 PAQ of PAPEL-HIG-12;
  ARROZ-1KG last moved on 2026-06-20. All other oracle dates and sets matched.
- An API-created partial warehouse and an additional divergent historical fact
  were each rejected by the actual loader with nonzero status. Full API-visible
  snapshots before/after each rejected run matched: no repair writes occurred.
  These probes used disposable validation data only. Host and both validation
  databases were removed successfully afterwards.
- The external dataset checksum manifest validates all 11 listed files. Its
  directory is not a Git repository; loader/docs/checksum changes remain there,
  outside the product commit and build.

The final diff was reviewed for module boundaries, shared contract preservation,
meaningful literals, documentation impact and excluded features. There are no
new dependencies, demo migration rows, AI registrations or cross-module context
references. These results establish local .NET behavior and published v0.11.1
conformance; they do not establish WMS-004 shared-contract or Java conformance.
