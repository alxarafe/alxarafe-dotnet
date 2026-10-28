# Usage

## Full validation

Docker and Docker Compose are required; host .NET, Bruno, Node and PostgreSQL are not.

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/clean-v0.13.0-checkout ./bin/check
```

The checkout must have the expected origin, no tracked/nonignored changes, HEAD and
v0.13.0 tag object at 919684db4f607a575397576b87577cd9582e1f86, resolving to commit
29bd8de3e5c5772fea5096ab2c60abc6a10e55f7. bin/check never modifies it.
CI uses the same authority and pinned release. Check executes restore/build/native
architecture and integration tests, clean migrations, isolated Host startup, full
shared Bruno, local Core Bruno (three requests), OpenAPI/persistence/secrecy checks
and cleanup. Any failure propagates nonzero.

Validation uses alxarafe_test and alxarafe_security_test for the control plane,
plus alxarafe_business_test and alxarafe_business_test_b for independent tenant
business storage. Existing development databases/volumes are preserved. The
validation Host and all four test databases are removed even after failure.
bin/test runs native tests through Docker;
bin/bruno runs isolated HTTP checks. These are components of bin/check, not substitutes.
No shared test is run against persistent development.

Testing plus Modules:ConformanceEnabled=true registers the two canonical dev
modules. Validation Compose enables it explicitly. Normal startup includes production WMS and AI;
Development alone never opts into canonical descriptors. Startup synchronizes their
initially enabled state through the normal pipeline. Shared conformance
changes/restores module states. Canonical descriptors expose no business or
mathematical endpoints.

Shared conformance executes the sole published collection, including UoM,
Storage Attribute and Item Family coverage, CORE capability discovery, synchronous
READ/AI assistant invocation, Generic Resource Runtime and effective navigation.
The optional candidate check below additionally verifies contextual presentation.
All three WMS catalogues and associations start empty
in the disposable validation database. Post-conformance SQL verifies retained data and enabled state.
See [AI-001 evidence](verification/ai-001-dotnet.md) and
[WMS-003 evidence](verification/wms-003-dotnet.md); earlier verification reports remain historical.

## Additional candidate verification

`contract.revision` and CI remain pinned to published immutable **v0.13.0**.
Development can explicitly validate an unpublished candidate in addition to it:

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/clean-v0.13.0-checkout \
ERBAS_CONTRACT_CANDIDATE_DIR=/absolute/path/to/clean-candidate-checkout \
ERBAS_CONTRACT_CANDIDATE_COMMIT=9919ad3725e50d63e7a19befbe0d81b4d91df0cc \
./bin/check
```

Both candidate variables are required together. The candidate must be an official
`alxarafe/erbas-contract` repository root, clean of tracked/nonignored changes,
with executable `bin/test` and HEAD exactly equal to the supplied full commit SHA.
There is no fetch, branch following, checkout modification, hardcoded candidate
version or remote mutable dependency in CI. A branch name is not accepted as a SHA.

The native suite runs first. Published shared conformance always runs before the
candidate. The check then stops the validation Host and recreates all four test
databases before running the candidate's single shared collection, local Bruno,
OpenAPI, persistence and sensitive-log checks again. Cleanup is mandatory on
success or failure. Logs distinguish `PUBLISHED_CONTRACT_CONFORMANCE` from
`UNPUBLISHED_CONTRACT_CANDIDATE` and `CANDIDATE_CONFORMANCE` with the exact SHA.
A candidate result is development evidence, not publication or formal adoption.
See [RESOURCE-PRESENTATION-001](resource-presentation-001.md).

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
Storage Attributes have no individual GET, master PATCH/DELETE, exclusive group,
item/location association or storage rule. Warehouse inventory is provided
separately by the [WMS inventory foundation](wms-inventory.md). See
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
it deletes development storage. A fresh volume creates alxarafe, alxarafe_security
and alxarafe_business. Existing volumes receive the synthetic business database
through the local `bin/up` bootstrap; existing shared business data is not moved.
Identity retains its existing initialization. Platform/AI usage remain in the
control plane; WMS migrates independently in tenant business databases through
central reference resolution. Runtime never falls back to ConnectionStrings__Wms.
See [tenancy](tenancy.md) for configuration, single-membership execution,
operator-controlled upgrades and migration ownership.

A local CORE administrator requires explicit `ERBAS_CORE_ADMIN_PASSWORD`;
`ERBAS_CORE_ADMIN_EMAIL` defaults to `admin@example.test`. The administrator seed
does not reset credentials or escalate an existing non-administrator account.
Validation always supplies its own generated administrator password.

An optional WMS development reader is enabled by explicit
`ERBAS_WMS_USER_PASSWORD`; `ERBAS_WMS_USER_EMAIL` defaults to `user@example.test`.
Compose maps these to `Wms:DevelopmentUser:Email`/`Password`. WMS contributes the
four warehouse/location/item/inventory READ claims through the existing generic
`DevelopmentUsersOptions` seed in Development only. No movements or WRITE claims
are granted. Testing and Production do not contribute this development profile,
even when configured. Without a password no reader is provisioned. Store local
passwords only in the ignored `.env` or your secret environment mechanism.
The AI module explicitly contributes `ai.assistant.invoke` to configured
Development users through the same generic options, without additional WMS rights.
See the [effective access matrix](wms-inventory.md#development-identities).

## API and diagnostics

Health is public process liveness, independent of database/modules. Auth login,
current identity, user/module administration, WMS catalogues, contextual capability discovery, synchronous READ invocation and AI assistant invocation implement v0.13.0.
POST /api/auth/register remains .NET-specific; portable clients must not depend on it.
The WMS read discovery permissions map explicitly to the CORE administrator policy. Authentication/authorization
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

Capability discovery is available to authenticated users at `GET /api/core/capabilities` and `GET /api/core/capabilities/{capabilityId}`. It exposes the three WMS list descriptors only when WMS is enabled and the caller has the corresponding read permission (currently mapped to the administrator policy).

## Capability invocation

`POST /api/core/capabilities/{capabilityId}/invocations` executes an effective READ
capability with a closed `{"input":{"offset":0,"limit":50}}` body. Authentication,
capability ID syntax and effective discovery/authorization precede body parsing;
unknown, disabled and unauthorized capabilities return the same masked 404 even
with malformed input. Successful results contain a server-generated `invocationId`,
the exact requested `capabilityId` and the provider collection. Invalid input
returns 400, provider failures and known invalid output return the same safe 500,
and all responses are `Cache-Control: no-store`. Audit logs retain the trusted
invocation ID, correlation ID, capability, module, subject, timestamp, outcome,
duration and safe error code without logging payloads.

## AI assistant

`POST /api/ai/assistant/invocations` accepts exactly `{"message":"..."}` after
authentication, the `ai.assistant.invoke` permission and the erbas.ai module gate.
The message is required, nonempty, not whitespace-only, contains no NUL or
unpaired surrogate and is limited to 4000 Unicode scalar values. The caller cannot
supply history, prompts, tools, model/provider settings, permissions or invocation
IDs. Responses are closed JSON with `Cache-Control: no-store`; only 401 carries a
Bearer challenge.

AI-API-001 exposes effective module READ primitives through a neutral controlled
router under published v0.13.0. Existing capabilities retain CORE-CAP-002 execution;
primitives have distinct typed provenance. See [design and validation](ai-read-primitives.md).
The server exposes only authorized READ tools. It re-evaluates module state and
permissions before every call, enforces finite turn/tool budgets and fails safely
without leaking provider or capability metadata. Successful CORE invocation IDs
appear in execution order in `toolsUsed`; the AI invocation ID is distinct from
every CORE ID. AI permission grants no business permission.

Production/Development Compose configuration maps these host variables into the
container:

```bash
AI_GROQ_API_KEY=...                   # required for the Groq adapter
AI_GROQ_MODEL=qwen/qwen3.8-27b        # optional override
AI_GROQ_BASE_URL=https://api.groq.com/openai/v1
AI_GROQ_TIMEOUT_SECONDS=60
```

Testing selects a deterministic adapter instead, so CI, `bin/test` and shared
conformance never call a live provider or require a key. Development and
Production select the Groq adapter; without a key, AI requests fail safely with
`assistant_execution_failed`. Live provider quality and timeout behavior are not
part of shared conformance.

## Current limits

No Item, storage rules, topology, inventory, external plugin loader, hot loading,
module installation/uninstallation, capability bus or production provisioning
workflow exists. AI persistence, conversations, memory, RAG, WRITE execution,
streaming, model selection and provider fallback remain unimplemented.
Future modules own their domain, resources, permissions, endpoints, persistence and
tests and cannot depend on canonical dev descriptors. See [architecture](architecture.md)
and [cleanup verification](verification/cleanup-001.md).
