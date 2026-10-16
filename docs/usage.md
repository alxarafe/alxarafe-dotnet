# Usage

## Full validation

Docker and Docker Compose are required; host .NET, Bruno, Node and PostgreSQL are not.

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/clean-v0.8.0-checkout ./bin/check
```

The checkout must have the expected origin, no tracked/nonignored changes, HEAD and
v0.8.0 tag at 9dcb1605bcddbd450d4c009285313a142ab9fb35. bin/check never modifies it.
CI uses the same authority and pinned release. Check executes restore/build/native
architecture and integration tests, clean migrations, isolated Host startup, full
shared Bruno, local Core Bruno (three requests), OpenAPI/persistence/secrecy checks
and cleanup. Any failure propagates nonzero.

Validation uses only alxarafe_test and alxarafe_security_test. Existing development
databases and volumes are preserved. The validation Host is removed and both test
databases are dropped even after failure. bin/test runs native tests through Docker;
bin/bruno runs isolated HTTP checks. These are components of bin/check, not substitutes.
No shared test is run against persistent development.

Testing plus Modules:ConformanceEnabled=true registers the two canonical dev
modules. Validation Compose enables it explicitly. Normal startup includes production WMS;
Development alone never opts in. Startup synchronizes their initially enabled state
through the normal pipeline. Shared conformance changes/restores both states.
They expose no business or mathematical endpoints.

Shared conformance executes the sole published collection: **494 requests / 2190
named checks**, including the earlier UoM and Storage Attribute coverage. All three WMS catalogues and associations start empty
in the disposable validation database. Post-conformance SQL verifies retained data and enabled state.
See [WMS-003 evidence](verification/wms-003-dotnet.md); earlier verification reports remain historical.

## WMS unit of measure

The UoM catalogue implements administrator-only POST and GET /api/wms/uoms.
POST accepts exactly code and description; unknown, duplicate, missing, null,
wrong-type and malformed JSON are invalid. POST accepts no query parameters.
Codes match ^[A-Z][A-Z0-9]{0,9}$ without trim or uppercase conversion.
Description trims exclusively edge ASCII space/TAB/CR/LF and then requires
1–255 Unicode scalar values. NUL and isolated surrogates are rejected.
Internal content, case and whitespace remain intact; stored text is never
translated. Both operations return the normalized persisted description.

GET reuses COLLECTIONS-001 defaults/bounds and the collection envelope, with
fixed ordinal code ASC ordering before PostgreSQL pagination. Only one offset
and one limit are allowed. Beyond-total and huge offsets preserve metadata
and return an empty items array.

Authentication precedes existing CORE admin authorization, then the automatic
module gate, then functional validation. Responses are JSON/no-store; only 401
carries a Bearer challenge. Invalid input returns 400 invalid_request;
duplicates return 409 wms_uom_code_conflict without changing the original.
CORE PATCH /api/modules/erbas.wms disables/re-enables the module on the next
request without restart and without deleting UoM data.

## WMS Storage Attributes

The same `erbas.wms` module exposes only POST/GET `/api/wms/storage-attributes`,
under the existing bearer/CORE administrator policy and dynamic gate. POST accepts
exactly `code` and `name`; success returns a persisted server UUID, code and name
with 201 and no Location. Strict JSON parsing rejects duplicate/additional fields,
wrong types/casing, non-object roots, empty/malformed bodies and trailing JSON.
POST accepts no query parameter.

Code matches `^[A-Z][A-Z0-9_]{0,29}$` without normalization, including full-string
checking after a trailing LF. `A`, `COLD_CHAIN`, `ZONE2`, `A_1` and `IS_FOOD` are
valid. Name removes only boundary SPACE/TAB/CR/LF and then requires 1–255 Unicode
scalar values; padding does not count. NUL and unpaired surrogates are invalid.
Internal whitespace, NBSP, EM SPACE and case are preserved. Accept-Language never
translates stored names or changes technical values/errors.

GET reuses CollectionPagination and OffsetJsonConverter: only offset/limit once,
defaults 0/50, limit 1–100, ordinal code ASC ordering before SQL slicing, total
before slicing and empty windows at/beyond total, including huge BigInteger offsets.
Exact duplicates return 409 `wms_storage_attribute_code_conflict` without mutation.
Authentication, administrator authorization and module gating all precede validation.
Disable/re-enable blocks/restores all three WMS catalogues immediately without restart
or lost data. Existing shared-error middleware supplies JSON/no-store and only
401 has a Bearer challenge; 503 has no Retry-After.

Migration `202610150001_StorageAttributes` follows the unchanged UoM migration
`202610140001_Uoms`, using the same WmsDbContext, connection and
`__WmsMigrationsHistory`. Its reversible second table contains only id/code/name;
code has C collation and a unique index. INSERT ON CONFLICT is race-safe; COUNT
and page share RepeatableRead. OpenAPI includes the two new operations with closed
HTTP DTO schemas, preserving pure Domain/Application and Host's entry-only reference.
Storage Attributes have no individual GET or master PATCH/DELETE. There is no
exclusive group, Item, location/zone association, storage rule, inventory or AI API. See
[verification and scope](verification/wms-002-dotnet.md).

## Item Families and associations (WMS-003)

POST/GET `/api/wms/item-families` use the same closed code/name representation,
Unicode normalization and pagination as Storage Attributes, with independent code
uniqueness. GET/POST/PUT/PATCH under
`/api/wms/item-families/{familyId}/storage-attributes` and DELETE of a specific
attribute manage the N:M relation. PUT accepts `{storageAttributeIds:[...]}` and
replaces the set; PATCH accepts `add`/`remove` arrays and validates all references
before all pre-state conflicts. Either operation is atomic, with no partial
writes on failure. POST returns the complete attribute and an exact relative
Location; DELETE removes only the pair.

Migration `202610160001_ItemFamilies` adds the family catalogue and FK-protected
composite-key join table to WMS's existing history. Earlier migrations remain
unchanged. A family row lock serializes association writes; PostgreSQL uniqueness
arbitrates concurrent catalogue creates. Disable/re-enable preserves every table.
All shared errors now use closed `{code, errors:[{code,...}]}` v0.8.0 envelopes;
association details identify resources and RFC 6901 body locations. Technical
codes and user names remain language invariant. See
[verification and exclusions](verification/wms-003-dotnet.md).

## Development

```bash
./bin/up
./bin/down
ERBAS_DOTNET_PORT=49081 ./bin/up
```

Development defaults to 127.0.0.1:48081 (container 8080); PostgreSQL is unpublished.
The volume persists across stops. Do not use bin/down --volumes to clean validation:
it deletes development storage. A fresh volume creates alxarafe and alxarafe_security;
Identity uses its existing initialization and Platform applies official EF migrations.
ConnectionStrings__Platform and ConnectionStrings__Security select the respective
contexts. WMS owns ConnectionStrings__Wms, its EF context, wms_uoms, wms_storage_attributes, wms_item_families,
wms_item_family_storage_attributes and
__WmsMigrationsHistory. Development uses the same physical alxarafe database;
Testing requires alxarafe_test and rejects every other WMS database. WMS applies
its migrations through normal module initialization, without changing Platform or
Security entities or migration histories. Existing obsolete experiment databases/tables are neither accessed nor
purged by startup. Retiring their projects does not rewrite applied migration history.

No module users or business permissions are seeded. A local CORE administrator
requires explicit ERBAS_CORE_ADMIN_PASSWORD; ERBAS_CORE_ADMIN_EMAIL defaults to
admin@example.test. Seed does not reset credentials or escalate an existing account.
Validation always supplies its own generated administrator password.

## API and diagnostics

Health is public process liveness, independent of database/modules. Auth login,
current identity, user/module administration and all three WMS catalogues implement published v0.8.0.
POST /api/auth/register remains .NET-specific; portable clients must not depend on it.
CORE admin does not imply functional module permissions. Authentication/authorization
precede parsing/lookup and functional gating. Local OpenAPI is available at
/openapi/v1.json and remains one schema, including all three WMS catalogues, without translated names.

Use Accept-Language: en/es; absent/unsupported headers fall back to English and
regional variants use native parent matching. Technical codes and fields remain stable.
See [internationalization](i18n.md) and [modularity](modularity.md).

```bash
docker compose ps
docker compose logs app postgres
./bin/bruno platform
```

Do not disclose bearer tokens, passwords or sensitive request data. Validation
withholds application logs in diagnostics and asserts known credentials are absent.
Native tests serialize test projects sharing the same ephemeral databases to avoid
schema/seed races. Test-only probes are never part of the production Host artifact.

## Current limits

No Item, storage rules, topology, inventory, AI, external plugin loader, hot loading, module
installation/uninstallation, capability bus or production provisioning workflow exists.
Future modules own their domain, resources, permissions, endpoints, persistence and
tests and cannot depend on canonical dev descriptors. See [architecture](architecture.md)
and [cleanup verification](verification/cleanup-001.md).
