# WMS-002 .NET: Storage Attributes and published contract v0.7.0

## Identity and preservation

.NET base: `73fc823c9b19052086305e899d5d6a492a3672b2` on main.
Branch: `feature/wms-002-dotnet-storage-attributes-v0.7.0`.
The pre-existing empty untracked `isBatchManaged` file is preserved and excluded
from this task's commit. No tracked/staged changes predated this task.

Published `alxarafe/erbas-contract` authority:

- Tag: `v0.7.0`.
- Annotated tag object: `c6cdbe0d90c63fb79b1bb36a96d67d822243ffdb`.
- Target and checkout HEAD: `76618b4d6776dca5e13466e7fde44af675d5b08f`.

The supplied sibling checkout is clean and is only read/executed. Contract, Java
and clients are unchanged. `contract.revision`, strict preflight and CI pin this
published release; no fetch/pull/checkout is performed by bin/check. Origin, root,
tag, immutable target, HEAD, cleanliness and executable-runner checks are retained.

## Domain, application and module boundaries

All work uses the five existing WMS projects. No new project/context/infrastructure
is introduced. Domain `StorageAttribute` has validated Guid/code/name and rejects
Guid.Empty. Exact ASCII codes follow `^[A-Z][A-Z0-9_]{0,29}$`, with an explicit
full-match length check for .NET's final-LF behavior. IS_FOOD is allowed and codes
are never trimmed, uppercased or otherwise normalized. A targeted CA1711 suppression
keeps the published domain term StorageAttribute: it denotes a catalogue concept,
not a CLR annotation; no analyzer is globally relaxed.

The sole domain extraction is internal `CatalogueLabel`, preserving UoM's exact
four-character boundary trim and Rune.DecodeFromUtf16 scalar algorithm. A single
255-scalar maximum is shared. NUL and malformed surrogate pairs are invalid;
valid supplementary characters count once. Padding does not count, and all other
spaces, internal content and case are retained. Existing UoM regression cases
remain unchanged.

`StorageAttributeCatalogue`, `IStorageAttributeStore`, `StorageAttributeWindow`,
`CreateStorageAttributeOutcome` and `CreateStorageAttributeResult` parallel UoM.
Application generates Guid values and exposes Created/InvalidRequest/CodeConflict,
with no ASP.NET Core/EF/PostgreSQL dependency. The existing `WmsModule` registers
the store/catalogue and maps both endpoint sets. Its ID remains erbas.wms and its
functional dependency list stays empty. Host still knows only the entry assembly;
Core and platform/security models own no WMS entities.

## EF storage and frozen migrations

`WmsDbContext` gains StorageAttributeRow/StorageAttributes, with the same WMS
connection and `__WmsMigrationsHistory`. Live constants explicitly distinguish
UomTable/UomPrimaryKey/UomCodeIndex from the corresponding StorageAttribute names.
Old aliases remain fixed to UoM solely for source compatibility with the unchanged
historical migration. They never select different tables.

`202610150001_StorageAttributes` creates only `wms_storage_attributes`:

```text
id UUID PRIMARY KEY
code TEXT COLLATE "C" NOT NULL UNIQUE
name TEXT NOT NULL
```

Down removes only that table. Its frozen target model calls the unchanged first
migration's frozen model and adds frozen Storage Attribute metadata. The snapshot
points to the accumulated second model. The original 202610140001_Uoms file is
unchanged. Integration verifies fresh history, exact columns, nullability, PK,
unique index, collation, no pending model changes and upgrade/downgrade/re-upgrade
in an owned PostgreSQL schema while preserving the original UoM data.

`EfStorageAttributeStore` uses INSERT ON CONFLICT DO NOTHING, with PostgreSQL as
concurrent uniqueness authority. A duplicate never updates the original. COUNT
and SQL code ASC/LIMIT/OFFSET run under one RepeatableRead snapshot. Offset is
BigInteger and checked against total before any long conversion, so arbitrarily
large offsets produce an empty window without loading all rows.

## HTTP, OpenAPI, security and i18n

`StorageAttributeEndpoints` maps only POST/GET `/api/wms/storage-attributes`.
JsonDocument plus explicit root/field/type/count checks close the POST input and
exclude duplicated fields. Parsing a complete document rejects trailing JSON;
native HTTP tests cover this and the retained UoM trailing-token regression.
GET reuses CollectionPagination and OffsetJsonConverter. Successful POST has
closed id/code/name, 201 and no Location. Duplicate 409 is the exact closed
`wms_storage_attribute_code_conflict`; invalid input is `invalid_request`.

Closed HTTP DTOs keep serialization/OpenAPI annotations out of Domain. The HTTP
adapter reuses the exact Microsoft.AspNetCore.OpenApi 10.0.0 / Microsoft.OpenApi
2.7.5 versions already used by Host for native endpoint operation metadata, without
adding Host references to module internals or changing any existing package version.
See [official endpoint transformer documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-10.0).
Input name has no raw maxLength; output describes the normalized scalar limit.
Native tests verify both WMS paths, only create/list on the new path, closed schemas,
required fields, limits, pagination metadata, security and inherited 503 metadata.

The group uses CoreAdministration.Policy, WithSharedContractErrors and the existing
automatic erbas.wms gate. Authentication then admin authorization precede the gate,
then parsing/validation/operation: invalid requests produce 401/403/503/400 for the
corresponding actor/state. Disabling blocks both catalogues immediately; re-enabling
needs no restart and retains both datasets. Shared middleware supplies application/json
and no-store, only 401 carries WWW-Authenticate Bearer, and 503 omits Retry-After.
Names are persisted user content: Accept-Language never changes id/code/name,
properties or technical errors. No resx or new human messages are introduced.

## Verification

The final complete command passed with exit 0 on 2026-10-15.

| Check | Actual result |
| --- | --- |
| .NET restore/build/analyzers | Passed, zero warnings and errors in the final native build |
| Architecture/Domain/Application suite | 86 passed, zero failures/skips |
| PostgreSQL/EF/HTTP suite | 215 passed, zero failures/skips |
| Total distinct native cases | 301 passed |
| Added pure coverage | 30 Storage Attribute domain cases + 2 application cases; 1 additional layer-reference architecture test |
| Added integration | 12 targeted EF/PostgreSQL/HTTP/OpenAPI facts |
| Frozen models and migrations | Fresh history exactly Uoms + StorageAttributes; no pending model changes; upgrade/downgrade/re-upgrade retained UoM data |
| Fresh-install SQL | Both migrations, exact columns in both tables and both catalogues empty before shared conformance |
| Full live shared Bruno | Passed: 266 requests / 1050 named checks, preserving the earlier 182 / 714 subset |
| Additional local Bruno | Platform: 3 requests passed, 1 named test and 5 assertions passed |
| Post-conformance SQL | Both WMS catalogues retained rows; erbas.wms enabled; both WMS migration entries and all prior Core/Security invariants passed |
| HTTP/OpenAPI probes | Passed for existing APIs and both WMS paths |
| Sensitive logs | LOG_SECRECY=passed |
| Cleanup | VALIDATION_DATABASE_CLEANUP=passed; validation Host removed and both temporary databases absent |
| Formatting | dotnet format in Docker, restricted to this task's C# files, exit 0 |
| Source/shell | git diff --check and bash -n bin/check passed |

No contract timeout 124 occurred. The published runner intentionally withholds
execution statistics along with sensitive diagnostics; counts were independently
verified in Docker from the same clean v0.7.0 collection mounted read-only,
including inherited folder checks. They were not made a backend success condition.
The complete shared collection ran without selecting only WMS-002 or skipping cases.

Two earlier attempts failed before shared conformance: first the naming analyzer
CA1711 rejected the contractual domain term, then the new upgrade test supplied a
historical twelve-digit migration ID directly as an EF target. The naming exception
is local and justified; the test now obtains the framework-native target name with
IMigrationsIdGenerator.GetName, preserving both historical migration IDs. Complete
validation was repeated after corrections and the scoped formatting pass. No
contract, runner deadline, historical migration, production schema or existing
HTTP behavior was altered to resolve these failures.

```bash
ERBAS_CONTRACT_DIR=../erbas-contract ./bin/check
git diff --check
bash -n bin/check
```

Domain/Application tests cover code grammar/boundaries, Unicode normalization,
invalid scalars, Guid.Empty, generated identities, outcomes and delegation without
mutating stored state on conflict. Architecture tests strengthen layer project
references and retain strict framework/Host entry-only/pure-layer checks. Native
integration uses real EF/PostgreSQL and HTTP/Identity, with targeted groups of
invalid inputs rather than repeated bootstrap for every JSON row.

The full published shared collection is mandatory against the isolated live Host;
new Storage Attribute requests are not selected separately. Fresh-install SQL
requires both exact tables/column sets and both migrations, with both catalogues
empty. Post-conformance SQL requires retained UoM and Storage Attribute rows,
enabled WMS and both history entries, without weakening prior Core/Security checks.
Generated validation credentials and detailed Bruno diagnostics remain withheld.
Cleanup removes only the validation Host and both disposable databases, preserving
development databases, PostgreSQL service, volumes and caches according to the
existing validation lifecycle.

## Scope and reviews

Health, auth, users, COLLECTIONS-001, module administration, canonical dev modules,
UoM grammar/normalization/errors/JSON/order/pagination, WMS connection/history and
module ID/dependencies retain their behavior. Old WMS verification is historical
and unchanged. Meaningful new values use scoped domain/HTTP/storage constants,
framework HTTP facilities and existing collection bounds. Frozen migration literals
are deliberate immutable metadata. No general constants or parsing framework is added.

No exclusiveGroupCode, individual GET, PATCH/DELETE, ItemFamily, Item, family links,
location/zone attributes, storage rules, topology, inventory, AI, new RBAC, Java,
frontend or contract changes are included. No push/PR/merge/tag/release/deployment
is authorized or performed.
