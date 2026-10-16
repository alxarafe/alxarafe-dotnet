# WMS-003N: Item Families and atomic Storage Attribute associations

## Authority and scope

Validated on 2026-10-16 from base
`a055d99097789ce3c6c48161b6cc939fc2b6d1cc`, on
`feature/wms-003-dotnet-item-families-v0.8.0`. The implementation is left
uncommitted for review, as requested. The unrelated pre-existing untracked empty
file `isBatchManaged` is preserved.

`contract.revision` and CI now select published `v0.8.0`:

- Annotated tag object: `303a61b0207d3984780539da4068ab68c1264a8d`.
- Commit: `9dcb1605bcddbd450d4c009285313a142ab9fb35`.

The supplied checkout's root, accepted GitHub origin, cleanliness, exact tag object,
resolved commit and matching HEAD are checked before validation. Validation never
modifies that checkout. Shared OpenAPI is the formal authority and the existing
single shared Bruno collection supplies conformance; neither is copied or changed.

## Implementation

| Operation | Behavior |
| --- | --- |
| POST `/api/wms/item-families` | Closed code/name input, committed canonical UUID representation; exact-code duplicate 409. |
| GET `/api/wms/item-families` | COLLECTIONS-001 page, code ordinal ascending, total before slicing. |
| GET `/api/wms/item-families/{familyId}/storage-attributes` | Same collection metadata, complete associated Storage Attributes. |
| POST on that association path | One pair, 201, complete attribute and exact relative Location; duplicate 409. |
| PUT on that association path | Closed storageAttributeIds wrapper; atomic, idempotent exact replacement, including empty set; 204. |
| PATCH on that association path | Strict add/remove arrays, master existence before every pre-state conflict, atomic 204 or structured multi-error. |
| DELETE `/api/wms/item-families/{familyId}/storage-attributes/{storageAttributeId}` | Pair-only removal, empty 204; differentiated missing-family/master/pair 404. |

Domain/Application remain independent of ASP.NET, EF and localization. ItemFamily
reuses CatalogueLabel for ASCII-edge normalization and Unicode scalar validation.
CatalogueCode extracts the identical Storage Attribute/family syntax without
changing UoM's deliberately different rule. Existing strict JsonDocument parsing,
CollectionPagination and OffsetJsonConverter are reused. Exact canonical, non-nil
UUID syntax is checked before resource lookup. PostgreSQL C collation supplies
ordinal ordering, with parameterized SQL pagination and snapshot-consistent totals.

The existing WMS entry registers application/store services and maps endpoints
through the existing module gate. Authentication/current identity, administrator
authorization and enabled state precede body/query/path validation. Disable and
re-enable take effect without restart and retain all catalogue/relation data.

### Persistence and upgrade

`202610160001_ItemFamilies` adds `wms_item_families` (UUID PK, non-null code/name,
unique C-collated code) and `wms_item_family_storage_attributes` (composite PK,
two restrictive FKs and the reverse-lookup index). It belongs to WmsDbContext and
`__WmsMigrationsHistory`, on the existing WMS connection. Earlier migrations are
unchanged; Platform/Security models and histories acquire no business ownership.

Catalogue INSERT ON CONFLICT uses PostgreSQL uniqueness as the race authority.
Every association mutation locks its family row before reading existing state.
Existence and all pre-state conflicts are collected before writes. Removal and
insertion commit in one transaction; returning any error rolls back without
partial persistence. Concurrent association POSTs serialize and see prior commits.
GET pages use RepeatableRead; mutations use ReadCommitted plus the family lock.

The upgrade test creates an isolated PostgreSQL schema with exactly the two old
WMS migrations, inserts UoM and Storage Attribute rows, then migrates twice. It
proves all original IDs/fields survive, only ItemFamilies is appended once, both
new tables start empty, no migrations/model changes remain pending, and the
composite key/two FKs belong to WMS. Existing UoM migration/upgrade tests remain.
No repair, baseline, reset or migration-history rewrite is used.

### Global errors

A narrow generic ContractError/ContractErrorDetail representation supplies the
required code plus nonempty errors array, omitting absent resource/resourceId/path.
Legacy Login/User/Module/UoM/Storage Attribute and disabled responses gain only the
required envelope; the shared authorization handler adds matching technical details.
No success shape, status, authorization or error code changes beyond v0.8.0.
Association errors carry deterministic resource context and RFC 6901 pointers,
including repeated missing references across add/remove. No human messages,
ProblemDetails fields, stack traces or SQL diagnostics enter shared errors.
The .NET-specific registration extension keeps native localized validation.

## Validation evidence

Executed exclusively through the existing Docker procedures:

```bash
ERBAS_CONTRACT_DIR=<clean-exact-v0.8.0-checkout> ./bin/check
```

The final authoritative execution exited **0**, taking **183.81 seconds**
(wall-clock time measured with `/usr/bin/time -p`).

| Validation | Result |
| --- | --- |
| Solution restore, build and publish | Passed, zero build warnings/errors |
| Domain/unit/architecture project | 118 passed, 0 failed/skipped |
| Real PostgreSQL / Host integration project | 232 passed, 0 failed/skipped |
| Native total | 350 passed, 0 failed/skipped |
| Fresh composed installation | WMS_FRESH_INSTALL=passed; separate histories, empty catalogues/relations |
| Existing WMS-001/002 upgrade | Passed; retained rows and exactly-once additional migration |
| Concurrent catalogue/pair HTTP creates | One 201 and one 409 each, one persisted row/pair |
| Atomicity | Failed PUT/PATCH followed by GET preserve the prior exact set |
| Shared v0.8.0 conformance | Passed; 494 requests / 2190 named checks, one collection |
| Local platform Bruno | 3 requests, 1 named test and 5 assertions passed |
| Health and generated OpenAPI | Passed |
| Post-conformance database checks | CORE administrator, WMS data and enabled state retained |
| Log secrecy | LOG_SECRECY=passed |
| Cleanup | Validation Host removed; VALIDATION_DATABASE_CLEANUP=passed |
| Formatting | dotnet format in Docker on newly added C# files passed |
| Shell / whitespace | bash -n bin/check and git diff --check passed |

The shared runner reports only redacted success. The 494/2190 inventory was also
recounted from the immutable collection, including inherited folder checks.
Focused native tests cover Unicode supplementary scalars, NUL/unpaired surrogates,
strict JSON/query/UUID rules, pagination, normalized persistence, concurrency,
security/gating precedence, both locales and differentiated association errors.
Architecture guards retain pure WMS layers, framework independence, Host entry-only
composition and separate persistence ownership. All previous tests are retained.

## Change inventory

15 created, 29 modified, none deleted. No files are staged.

### Created

- `src/Alxarafe.AspNetCore/ContractError.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Application/ItemFamilyAssociations.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Application/ItemFamilyCatalogue.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Domain/CatalogueCode.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Domain/ItemFamily.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Http/ItemFamilyAssociationEndpoints.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Http/ItemFamilyEndpoints.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Infrastructure/EfItemFamilyAssociationStore.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Infrastructure/EfItemFamilyStore.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Infrastructure/Migrations/202610160001_ItemFamilies.cs`
- `tests/Alxarafe.ArchitectureTests/ItemFamilyTests.cs`
- `tests/Alxarafe.Host.IntegrationTests/ContractErrors.cs`
- `tests/Alxarafe.Host.IntegrationTests/ItemFamilyAssociationTests.cs`
- `tests/Alxarafe.Host.IntegrationTests/ItemFamilyTests.cs`
- `docs/verification/wms-003-dotnet.md`

### Modified

- `.github/workflows/ci.yml`
- `AGENTS.md`
- `README.md`
- `bin/check`
- `contract.revision`
- `docs/README.md`
- `docs/architecture.md`
- `docs/i18n.md`
- `docs/modularity.md`
- `docs/usage.md`
- `src/Alxarafe.AspNetCore/ModuleStateEndpointFilter.cs`
- `src/Alxarafe.Host/LoginEndpoint.cs`
- `src/Alxarafe.Host/UserEndpoints.cs`
- `src/Alxarafe.Security.AspNetCore/SharedContractAuthorization.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Domain/StorageAttribute.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Http/StorageAttributeEndpoints.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Http/UomEndpoints.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Infrastructure/Migrations/WmsDbContextModelSnapshot.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Infrastructure/WmsDbContext.cs`
- `src/Modules/Wms/Alxarafe.Modules.Wms.Module/WmsModule.cs`
- `tests/Alxarafe.Host.IntegrationTests/LocalizationHttpTests.cs`
- `tests/Alxarafe.Host.IntegrationTests/LoginOpenApiTests.cs`
- `tests/Alxarafe.Host.IntegrationTests/ModuleCompositionTests.cs`
- `tests/Alxarafe.Host.IntegrationTests/ModuleGatingTests.cs`
- `tests/Alxarafe.Host.IntegrationTests/ModulesApiTests.cs`
- `tests/Alxarafe.Host.IntegrationTests/StorageAttributeTests.cs`
- `tests/Alxarafe.Host.IntegrationTests/UsersApiTests.cs`
- `tests/Alxarafe.Host.IntegrationTests/UsersInvariantTests.cs`
- `tests/Alxarafe.Host.IntegrationTests/WmsTests.cs`

## Exclusions and review

No contract, Java, client/frontend or .NET framework/dependency/base-image changes.
No master update/delete, Item/product, exclusive groups, inventory, WMS-004,
new module, RBAC, event bus or loading mechanism. Docker/Compose conventions and
cleanup ownership are unchanged. Meaningful new protocol/domain/storage literals
use framework constants or narrowly named WMS constants. Current documentation
is updated; historical reports/ADRs are preserved. No commit, push, PR/MR, merge,
tag, release, publication or deployment was performed.
