# Resource Platform

RESOURCE-METADATA-001 implements the first Resource Platform foundation in
`Alxarafe.Resources`. Backend metadata is the semantic authority; framework-specific
representations are derived from it. This is a metadata model, validator and
read-only registry. RESOURCE-RUNTIME-001 adds generic backend execution in the separate
`Alxarafe.Resources.Runtime` boundary. RESOURCE-API-001 projects HTTP/metadata/OpenAPI through
`Alxarafe.Resources.AspNetCore`; this is not a low-code/business-rule engine.

## Execution boundary and current scope

The generic HTTP controller role is implemented idiomatically by Minimal API
`ResourceEndpoints` (CRUD/metadata) and `ResourceActionEndpoints` (actions), not by a
parallel MVC ResourceController. Both use the same frozen registry as orchestration:

```text
HTTP → ResourceEndpoints / ResourceActionEndpoints
     → ResourceRuntime / ResourceActionRuntime
     → registered ResourceDefinition + shared semantic validation
     → typed repository / handler ports → module-owned adapters
```

The HTTP layer parses/projects transport and maps structured errors; it does not
validate resource-specific numeric ranges or choose concrete business entities.
Definitions contain no execution state. Records, queries, action targets and inputs
are separate runtime values. Registration, invalid-definition/unknown-resource
failures, constraints, structural relation resolution and action execution are covered
by behavior tests. This is a usable generic infrastructure boundary, not a production
ERP module or universal persistence engine. Production resource contribution/adapters
remain module-owned; the normal catalog is currently empty.

## Boundary and composition

```text
Modularity: ModuleRegistry (captured identities)
                  ↑ consumed by
Resources: IResourceDefinitionProvider → ResourceRegistry
                  ↑ contributed by
Modules / explicit typed application composition
```

Core and Modularity do not reference Resources. Resources references only Modularity
and the standard library: no ASP.NET, EF, PostgreSQL, business module, OpenAPI, UI or
AI provider dependencies. WMS and AI contribute no production ResourceDefinitions
in this phase; their existing endpoints/persistence/authorization are unchanged.

`IResourceDefinitionProvider.GetResourceDefinitions()` returns typed definitions.
Register providers through ordinary typed DI during composition. The Host eagerly
constructs `ResourceRegistry` immediately after building services, before database
initialization and HTTP. Invalid definitions prevent startup, including when a
provider is supplied only in validation composition. The normal application currently
has an empty resource catalog. Metadata endpoints project this catalog; there is no
resource administration endpoint.

The registry validates local definitions, collects all resources, validates relation
references, computes hashes and freezes. Providers are called only during construction;
changing their input collections later cannot change the catalog. `All` uses ordinal
resource-ID ordering; `Require`, `TryGet` and `DefinitionHashes` are read-only access.
There is no incremental/runtime registration API.

Every definition must name an owning module in the authoritative module catalog. A
provider that is itself a registered module must use that module's captured identity,
even if its descriptor getter later changes. Separate providers are explicit trusted
composition and identify owners through each definition, which must reference the
catalog. Registration grants no membership or permission and selects no tenant or DB.

## Semantic types and exact values

The initial catalog is BOOLEAN, STRING, TEXT, INT8, UINT8, INT16, UINT16, INT32,
UINT32, INT64, DECIMAL, UUID, DATE, TIME, DATETIME, ENUM and BINARY. Relations are
separate. Semantic type is not CLR storage type; no entity/DTO/CLR type mapping is
stored. UINT32 covers 0..4294967295 regardless of the eventual storage representation.

Integer domains expose exact intrinsic minimum/maximum and unit step through
`NumericDomain.Intrinsic`. DECIMAL is an exact, initially unbounded base-ten domain,
not CLR `decimal`. UINT8's intrinsic domain is exactly 0..255: -1/256 fail, 0/255
succeed. Field constraints can further restrict that domain. Scalar validation,
CREATE/PATCH and HTTP behavior tests pin these boundaries without byte-range logic
in the HTTP adapter. `ResourceNumber` uses arbitrary-precision integer coefficient and
scale, normalizes trailing zeros and compares without floating-point rounding.
Fixed decimal precision/scale may exceed CLR decimal limits. Decimal literals use
plain base-ten notation; exponents, NaN and infinities are not accepted.

Defaults are a closed set of validated semantic shapes: null, Boolean, text, exact
number, UUID, date, time, instant and immutable base64 binary. ENUM declares distinct
text values. DATETIME is an instant, canonicalized to UTC. A missing default differs
from an explicit null default. There are no arbitrary object values or persistence
records. Temporal default values use BCL calendar/time representations; they do not
choose a database representation.

## Constraints and fields

`FieldDefinition` declares name, semantic type, nullability, required/read-only/
immutable flags, typed default, constraints, filterable/sortable/searchable flags,
enum values and annotations. Collections are copied and exposed read-only. Field
names use ASCII letters followed by letters/digits/underscore, with ordinal identity.

- `NumericConstraints`: inclusive/exclusive bounds and positive `multipleOf`.
- `TextConstraints`: nonnegative minimum/maximum Unicode scalar length and pattern.
- `DecimalConstraints`: positive precision, scale from zero through precision, and
  optional numeric restrictions. Its domain is ±(10^precision−1)×10^−scale with the
  corresponding decimal step.

Effective domains are intrinsic domains intersected with field restrictions. A field
cannot declare bounds outside its intrinsic domain, even if another bound would
later narrow the result. UINT8 max 300/min −1 are invalid; UINT8 0..10 is valid.
Empty intervals and discrete/multiple intersections with no representable value fail.
Integer defaults must be integral; defaults must satisfy effective range, step,
precision/scale, lengths, patterns and enum membership. Constraint/type mismatches fail.

Patterns are culture-invariant and use the supported non-backtracking regex subset;
lookarounds/backreferences and malformed patterns are rejected. Default matching has
a bounded timeout. This is a documented metadata dialect, not an OpenAPI-derived
schema; future adapters must preserve its semantics or reject unsupported mappings.
Text defaults/annotations must be valid Unicode. Searchable currently applies only
to STRING/TEXT. Metadata alone declares capabilities; Runtime validates and executes
queries through adapters.

Required means required client input: required+nullable and required+readOnly are
contradictory. Immutable prevents changing the business value after creation; readOnly
means client input cannot set it. The single-field `IdDefinition` must reference a
non-nullable immutable UUID, string or integer field. Server-assigned IDs are read-only;
client-assigned IDs are required. Composite identifiers are deferred.

## Operations, security, relations and actions

Supported operations are explicitly declared LIST, GET, CREATE, UPDATE and DELETE.
Absent means unsupported. Exactly one `OperationSecurityRule` is required for each
supported operation and none for unsupported operations. Its nonempty, distinct dotted
lowercase permission identifiers are all required. This follows existing technical
permission naming conventions and also accepts two-part identifiers such as
`customer.read`. Metadata declares permissions; it does not grant or enforce them.
Existing RBAC remains authoritative.

`RelationDefinition` declares name, kind, target resource, local/target fields, optional
display field and required flag. MANY_TO_ONE is implemented. References are checked
after collection, independent of provider order; field types must match, including
enum domains. Required relations cannot use nullable local fields. Valid circular
business relations are allowed and are unrelated to module dependency cycles.
No joins, expansion or relation endpoints are implemented. Target uniqueness beyond
explicit resource identifiers is a future runtime/domain responsibility.

`ActionDefinition` declares technical name, ENTITY/COLLECTION/SELECTION scope,
permission, selection requirement and annotations. SELECTION must require selection;
ENTITY uses its entity context and COLLECTION has no selected-record requirement.
Names/permissions/scopes and duplicates are validated. Actions are metadata only,
with execution supplied through the typed Action Runtime described below.

## Typed annotation vocabularies

Annotations have vocabulary, stable term name and a typed value. An explicitly
registered `AnnotationVocabulary` defines its terms, allowed targets (resource,
field, action) and value shapes: text, Boolean, exact number or field-name list.
Unknown terms, wrong shapes, duplicate keys, invalid targets and missing field
references fail registration. There is no `Dictionary<string, object>` or free-form
uncontrolled annotation payload.

Small presentation-neutral built-ins prove the mechanism: `Common.Label`,
`UI.Hidden` and `UI.SelectionFields`. Modules/subsystems can add vocabularies without
changing Core. Selection-field order is intentional presentation metadata. These
terms express intent, never Angular components, CSS/classes or component-library
names; e.g. undeclared `UI.MatInput` is rejected. Rich UI/security/capability and any
future AI vocabulary belong to their own integrations; AI is not privileged.

## Validation diagnostics and canonical DefinitionHash

`ResourceValidationException` identifies ResourceId, Path, typed Code and a diagnostic
message. Duplicate IDs/fields/relations/actions, unknown owners, malformed declarations,
invalid defaults/constraints/security/annotations and unresolved relations fail eagerly.
The exception contains no business execution, HTTP response or authorization decision.

Each validated resource gets a lowercase SHA-256 DefinitionHash from a deliberate
UTF-8 JSON semantic projection marked `erbas.resource-definition.v3`. It includes
identity/version/owner/ID field, fields and effective constraints/defaults/capabilities,
relations, operations/security, actions/parameters, presentation and typed annotations (including used term
shapes/targets). It includes no CLR type/assembly name, provider implementation,
object hash code, DI order or runtime state.

Object property order is fixed. Fields, relations and actions are sorted by technical
name; action parameters are also sorted by name; operations use declared semantic enum order; permission sets, enum values and
annotation keys use ordinal order. Numeric spelling and equivalent discrete bounds
are normalized; UUIDs use canonical form, instants UTC and binary canonical base64.
Explicit annotation field-name list order remains significant. Text and regex
expressions retain their declared meaning; the hash is not a theorem prover for
arbitrarily equivalent patterns. Relation targets are hashed by referenced identity
and field names, not recursively: changing a target definition changes its own hash.
Consumers needing a whole catalog fingerprint must combine the relevant resource
hashes in a future, explicit dependency policy.

RESOURCE-ACTIONS-001 upgraded v1 to v2 for parameters and annotation targets.
RESOURCE-UI-METADATA-001 deliberately upgrades v2 to v3 for optional presentation.
All prior hashes invalidate once, including resources without a form; absent
presentation has an explicit canonical null. Layout tabs/sections/groups/fields/action
references retain declared order because it is meaningful. Layout moves, labels,
collapsibility, action placements and behavior changes alter the hash. The v3
minimal-resource golden vector is
`987e48e21244d1e3a678a6e2ffeafb35d0c485204ab1e55ad55f6c0aedc08d2c`.
Golden-vector tests pin the canonical format; insertion order, current culture and
provider identity cannot alter equivalent hashes. Semantic field, constraint,
permission, action, relation, version and annotation changes alter hashes. Changes
to the canonical projection require deliberate format/version review.

### Future frontend metadata cache coherence

DefinitionHash, already implemented by RESOURCE-METADATA-001, will also support
frontend metadata caching/coherence:

```text
cached hash == backend hash → cached metadata remains valid
cached hash != backend hash → fetch updated metadata
```

A metadata change does not imply Angular source regeneration. The future Core FE
runtime will render updated metadata directly. Cache dependency handling must respect
the per-resource relation/hash semantics described above. Metadata fetching, cache
invalidation in Core FE remain future work; backend ETag coherence is implemented below.

## Generic backend Resource Runtime

RESOURCE-RUNTIME-001 implements generic LIST, GET, CREATE, partial UPDATE and DELETE
in the separate `Alxarafe.Resources.Runtime` project/namespace. It references Resources
and the neutral Core/Modularity contracts; it has no ASP.NET, EF, SQL, WMS, AI or UI
dependency. The semantic Resources project remains independent of the runtime.
`ResourceValueValidator` exposes the existing semantic value checks for reuse;
constraint/type definitions and numeric/pattern validation are not duplicated.

> ResourceDefinition does not define database schema and Resource Runtime does not
> own module persistence.

### Records, identifiers and repository composition

`ResourceRecord` is an immutable ordinal snapshot of field names to existing typed
`ScalarValue` values. Missing entries and explicit `NullValue` remain distinct. There
is no object/dynamic/JSON-DOM business representation. Adapters map these boundary
records to normal typed domain objects.

`ResourceIdentifier` carries the declared ID field name and a typed scalar value.
GET/UPDATE/DELETE validate both against the registered ID field and its intrinsic
and declared constraints. There are no composite or opaque-object identifiers.

`IResourceRepository` defines typed asynchronous List/Get/Create/Update/Delete ports.
List returns `ResourcePage`; Get/Update return null and Delete returns false when the
record is absent. Create/Update return the complete persisted record. Ports expose
no HTTP, DbContext, EF entity, SQL, connection string or tenant selector.

The singleton `ResourceRepositoryCatalog` validates explicit resource/scope bindings
against resource ownership and the module's persistence declaration. Duplicate,
unknown or contradictory bindings fail during startup. Tenant modules require Tenant
bindings; Platform modules require Platform. Both modules need an explicit per-resource
choice. None modules may use Platform/non-tenant execution for computed adapters;
this grants no database or persistence mechanism. Scope is fixed composition metadata,
never inferred from a record, annotation or the presence of a current tenant.

`ResourceRepositoryRegistry` is scoped and snapshots typed repository instances by
their declared ResourceId. It rejects duplicate, unbound or unknown repositories and
captures one ResourceId for both binding validation and association, then checks
identity again before dispatch. Repositories may not change identity after
registration. The Host validates one scope before initialization/HTTP; each request
gets its own scoped adapters. Adapter constructors must be side-effect-free: resolve
connections only inside operations through the appropriate infrastructure. Bindings
and validators have no normal-request mutation API. An attempted operation without
an adapter returns RepositoryNotRegistered; metadata-only resources need not have an
executable adapter yet. No production WMS repositories are registered by this task.

### Public execution API and common pipeline

The public methods are `ListAsync(resourceId, query)`, `GetAsync(resourceId, identifier)`,
`CreateAsync(resourceId, values)`, `UpdateAsync(resourceId, identifier, patch)` and
`DeleteAsync(resourceId, identifier)`, with optional cancellation tokens.

The common orchestration order is:

```text
registered resource lookup
    ↓
operation declared?
    ↓
existing ModuleStateService: module enabled?
    ↓
fixed execution scope; existing IRequestTenantContext when Tenant is required
    ↓
authoritative permission authorization
    ↓
identifier / query / mutation validation
    ↓
repository resolution; current-record read for UPDATE only
    ↓
resource-specific mutation business validators
    ↓
repository operation
    ↓
output record/page contract validation
    ↓
neutral result
```

An unbound Both resource fails closed with RepositoryNotRegistered while resolving
its ambiguous scope, before authorization and without repository access. No scope
is guessed. All other request validation/persistence follows the guards above.
Administrative disable leaves modules/resources registered and technically initialized,
but every operation is denied. Unsupported operations fail before module/tenant/auth
or repository activity; runtime enforcement does not rely on HTTP routing.

> Tenant and permission context are authoritative infrastructure inputs and cannot
> be supplied by a Resource Runtime caller.

The runtime uses existing `IRequestTenantContext.RequireAsync` and `ModuleStateService`,
not parallel tenancy/state stores. The Host's scoped `IResourceAuthorization` adapter
obtains the current request principal, re-resolves persisted Identity through
`ICurrentSecurityContext`, and evaluates every metadata-declared permission through
the existing native policy provider/authorization service. Unknown policies deny.
Request permission claims cannot replace persisted claims; revocation is visible to
a later invocation in the same request scope. Metadata never registers/grants rights.

Tenant execution requires authoritative active membership and fails before repository
operations when unavailable. Tenant adapters must use `ITenantDatabaseResolver` to
resolve/revalidate their actual business database. Platform execution never requires
a tenant merely because one is present. Scope declarations do not route databases;
module adapters own that mapping. No caller-facing tenant, permissions, module-enabled
or database-reference parameter exists.

### Create, patch and delete semantics

CREATE rejects unknown/read-only fields, wrong types, invalid enum values, nullability
and intrinsic/declared constraint violations. Immutable writable fields are allowed
at creation. Defaults apply only to absent fields, including server-owned defaults;
explicit null is validated as null. Defaults are checked again before use. Required
client inputs must be present after default application. Server-generated read-only
fields may be absent until persistence supplies them. Non-required omitted values
remain absent unless a default exists; no value is coerced.

UPDATE is a patch. ID, read-only and immutable fields cannot be supplied, even with
the previous value. All supplied values are validated before reading the existing
record. Omitted fields remain unchanged, and create defaults are not applied. An
internal Get under UPDATE authorization obtains current data for merged-state/business
validation; this does not require declaring public GET or granting a separate read
permission. Only the validated patch is passed to Update, not the merged snapshot,
so concurrent unrelated fields are not overwritten by generic orchestration.

DELETE validates guards and identifier, runs business validators, then delegates to
the adapter. It defines no generic cascades. Missing records produce ResourceNotFound
for GET, UPDATE or DELETE. Referential restrictions remain domain/adapter concerns.

Repository outputs must contain the identifier and required fields, use only known
fields and valid scalar values, and match the requested identifier where applicable.
Invalid results are rejected rather than exposing unknown/sensitive adapter fields.
Page offset/limit/count/optional-total consistency and duplicate IDs are checked.
Post-persistence output checks do not roll back an already committed adapter mutation.

### Bounded semantic queries

`ResourceQuery` has nonnegative Int64 offset, a default limit of 50 and maximum 100,
ordered typed filters/sorts and optional search text. Totals are nullable; no count
query is required. Defaults align with current project paging conventions without
importing ASP.NET pagination types into the runtime.

- Filters support Equal/NotEqual, ordered comparisons (LessThan, LessThanOrEqual,
  GreaterThan, GreaterThanOrEqual), Contains and StartsWith. Terms combine as AND.
- Every filter field must exist and be Filterable. Equality operands obey field
  type/constraints/nullability. Ordering supports numbers, text and temporal types,
  excluding null. Boolean/UUID/enum/binary support equality only.
- Contains/StartsWith apply to STRING/TEXT fragments, validated as Unicode text;
  fragments need not satisfy a whole value's minimum length/pattern. They are literal
  text, not SQL expressions or wildcard programs.
- Sort terms must name Sortable fields, have distinct fields and explicit
  Ascending/Descending directions. Caller order is preserved.
- Search requires at least one Searchable field and nonblank valid Unicode text,
  bounded to 512 UTF-16 code units. Adapters apply it only to Searchable fields.

The repository adapter owns implementation of validated filters, ordering/search and
stable pagination/tie ordering. The runtime does not inspect persistence entities,
add hidden sort fields, perform joins or implement database-specific full-text search.
Relations retain their metadata; local relation values receive normal field validation.
No related-resource fetch, expansion, cascade or action execution is automatic.

### Business validation, failures, audit and atomicity

> Declarative metadata validates structure and constraints. Contextual business rules
> remain module-owned.

Zero or more `IResourceOperationValidator` instances can contribute per resource.
IDs must be unique per resource; execution is deterministic ordinal ID order. Context
contains operation/definition, identifier and immutable input/current records as
appropriate. UPDATE exposes its merged candidate state; DELETE exposes its identifier
and module validators may use their domain services for additional reads. Issues stop
the operation before mutation. Validators run after authorization and cannot grant it.
There is no rules language or generic domain-rule interpreter.

`ResourceRuntimeException` carries ResourceId, a stable typed Code and read-only issues
with field/path, typed input Code and safe diagnostic message. Codes distinguish
ResourceNotFound, OperationNotSupported, RepositoryNotRegistered, ValidationFailed,
Forbidden, TenantRequired, ModuleDisabled, ExecutionFailed and InvalidRepositoryResult.
There are no HTTP statuses here. Cancellation preserves the caller token. Unexpected
adapter/infrastructure errors are replaced with a safe ExecutionFailed error, without
inner exception, SQL, connection strings or credentials.

Existing auditing is specific to capability/primitive invocations and HTTP outcomes;
there is no suitable generic mutation-audit port. Resource mutation audit integration
is deferred instead of fabricating capability events or adding an audit framework.
No resource values are logged by this runtime.

A repository/domain adapter owns atomic mutation and must enforce its contextual
invariants inside its transaction. The runtime's pre-read/validators do not provide
optimistic concurrency, locking or a transaction spanning validation and mutation.
Cross-resource workflows belong to domain services/actions; no EF/distributed
transaction coordinator is introduced.

WMS remains the acceptance domain, with typed tenant persistence, actual permissions,
append-only movements and read-only inventory views preserved. Runtime contracts
contain no WMS knowledge. In-memory test adapters prove generic execution; Host tests
reuse real Identity/membership/module state and the two existing PostgreSQL validation
databases to prove isolation without a second tenant selector. Resource API tests reuse the same real infrastructure
with test-only definitions and in-memory persistence adapters.

## HTTP Resource API and metadata projection

RESOURCE-API-001 adds `Alxarafe.Resources.AspNetCore`. This adapter references inward
into Resources/Runtime and existing ASP.NET security/error infrastructure. Neither
the semantic model nor Runtime acquires ASP.NET or OpenAPI dependencies. The Host
composes the adapter; Core, WMS and AI contain no Resource API knowledge.

### Routes and transport

All resource and metadata routes require the existing Bearer authentication and
persisted enabled-user checks. Generic CRUD delegates to the existing scoped Runtime:

| Method | Route | Result |
| --- | --- | --- |
| GET | `/api/resources/{resource}` | Page: `items`, `offset`, `limit`, optional `total` |
| GET | `/api/resources/{resource}/{id}` | Record, or not found |
| POST | `/api/resources/{resource}` | 201 record and `Location` |
| PATCH | `/api/resources/{resource}/{id}` | 200 complete record after partial update |
| DELETE | `/api/resources/{resource}/{id}` | 204, no body |

Resource IDs resolve exclusively through the frozen registry. There are no natural
aliases or per-resource controllers. Identifiers are parsed using the declared ID
field: UUID in D format, semantic integer JSON number syntax, or a string identifier.
The Runtime validates the parsed ID's domain and constraints before repository use.

Mutation bodies are JSON objects of field names to scalar values, not serialized
Runtime objects. Maximum body size is 65,536 bytes; duplicate keys, nested values for
scalar fields and malformed values are rejected. JSON text is decoded strictly,
including property names: malformed Unicode/surrogate escapes are transport 400
errors, not server failures. Valid surrogate pairs retain their exact text. The same
bounded decoder protects mutation/action bodies and typed JSON filters. Explicit DTO projections prevent
internal implementation properties from leaking. No transport coercion is performed:

| Semantic type | JSON transport |
| --- | --- |
| BOOLEAN | Boolean token |
| STRING, TEXT, ENUM | String; Runtime validates enum membership |
| Integer types, DECIMAL | Number token, parsed exactly including bounded exponent syntax |
| UUID | String, hyphenated UUID D format |
| DATE | String `yyyy-MM-dd` |
| TIME | String `HH:mm:ss`, optionally up to seven fractional digits |
| DATETIME | ISO string with explicit `Z` or numeric offset; output normalized to UTC |
| BINARY | Base64 string; output normalized base64 |
| Explicit null | JSON null, independently checked against field nullability |

Numeric transport uses ResourceNumber, not floating point or CLR decimal. Numeric
mantissas/exponents are bounded to 4,096 digits/places for parsing. Clients must use
an exact-number JSON parser when needed, especially for INT64 and DECIMAL; JavaScript
Number cannot represent every valid value. Quoted numeric strings are rejected.

POST leaves defaults and complete create validation to Runtime. PATCH maps only
supplied properties: omission leaves a field untouched; explicit null requests null.
It applies no create defaults and rejects ID/read-only/immutable mutations through
Runtime. PUT and relation expansion are not exposed; actions have a separate namespace. CRUD responses/errors
are `Cache-Control: no-store`.

LIST accepts only the following query parameters (names are case-sensitive):

- `offset`: one nonnegative Int64, default 0.
- `limit`: one integer, default 50, maximum 100; zero is rejected.
- `search`: one search string, bounded by Runtime to 512 UTF-16 code units and
  requiring Searchable fields.
- Repeated `sort`: field name ascending, or `-field` descending. Term order matters.
- Repeated `filter`: URL-encoded JSON object with exactly `field`, `operator`, `value`.
  Operators are `eq`, `ne`, `lt`, `lte`, `gt`, `gte`, `contains`, `startsWith`. Values
  use the field's JSON semantic type. Multiple filters combine as AND.

For example, before URL encoding, a filter is
`{"field":"active","operator":"eq","value":true}`. Runtime remains the whitelist
for fields/capabilities, operators, constraints, sorting and search. Unknown query
keys and repeated singleton keys fail; non-LIST CRUD accepts no query parameters.
There is no OData, executable expression syntax, SQL fragment or tenant selector.

### Errors and execution authority

The HTTP adapter parses transport, delegates, projects results and maps errors. The
Runtime pipeline described above still owns operation support, module/tenant/auth
guards, value validation, business validation and persistence dispatch. Parsing can
fail before Runtime guards, but never accesses persistence. Guard failures do not
invoke repositories. Existing `ContractError` remains the sole error envelope:
`code` plus `errors` containing stable issue `code`, `resource` and escaped field
`path` where applicable. The closed shared envelope has no message property, so
Runtime diagnostic message text is deliberately not added to the HTTP contract.

| Runtime failure | HTTP mapping |
| --- | --- |
| ResourceNotFound | 404 `resource_not_found` |
| OperationNotSupported | 405 `resource_operation_not_supported`, with `Allow` |
| ValidationFailed / invalid transport | 400 `invalid_request` with issue codes/paths |
| Forbidden | 403 `forbidden` |
| TenantRequired | Existing authoritative tenant denial code, normally 403 |
| ModuleDisabled | Existing 503 `module_disabled` |
| RepositoryNotRegistered | 503 `resource_repository_not_registered` |
| ExecutionFailed / InvalidRepositoryResult | 500 `resource_execution_failed` |

Authentication uses the existing 401 `unauthorized` envelope/challenge. Adapter
exception text, SQL, credentials and connection references are never projected.
For TenantRequired, the adapter consults the same scoped `IRequestTenantContext`
again only to retain its existing denial reason (including selection required or
configuration failure); it does not retry execution or choose a tenant.

Authoritative tenancy still requires exactly one active persisted membership in an
active tenant. Zero memberships deny; multiple active memberships return
`tenant_selection_required`. Membership revocation is re-evaluated on later requests.
User IDs, roles, permissions, arbitrary headers, resource fields, hostnames and query
strings cannot establish authority. No `X-Tenant-Id` selector is implemented.

> A tenant identifier may later be supplied by the client as a requested tenant
> selection, but it must never become authoritative by itself.

A future selector must authenticate the user, receive the requested tenant, validate
active persisted membership and active tenant, establish authoritative TenantContext,
then execute Runtime. Runtime continues receiving only validated infrastructure
context. Platform-scoped execution retains its existing independent scope.

### Metadata API and effective capabilities

`GET /api/meta/resources` returns deterministic compact discovery entries: id,
version, moduleId, exact structural definitionHash, effective operation information
and metadataPath. Disabled modules are omitted from normal discovery.

`GET /api/meta/resources/{resource}` returns explicit DTOs for identity/version/owner,
definitionHash, ID field, moduleEnabled, fields, effective constraints, defaults,
capabilities, enum values, typed annotations, relations, operation declarations and
actions. Known definitions remain structurally accessible to authenticated users,
including disabled modules; `moduleEnabled: false` indicates unavailability. Unknown
resources return the safe 404 envelope. Metadata access requires authentication but
not resource CRUD permission. Seeing a definition never grants execution rights.

Operations expose `declared` and `authorized` separately. Authorization is evaluated
against persisted permissions using the same scoped `IResourceAuthorization` adapter
as Runtime. It does not imply an active tenant, an enabled module, an executable
repository or passing business checks. Core FE must also consider moduleEnabled;
Runtime checks all guards again on execution. Required permission IDs, identity claims,
repository types, CLR names and database references are deliberately not exposed.

Actions expose name, scope, requiresSelection, authorized and annotations. Their
permission IDs are not exposed. There is no fabricated `available: true` value and
no contextual availability value in static metadata.

> Authorization controls visibility. Availability controls enabled state.

Core FE should normally hide unauthorized actions, render authorized but unavailable
actions disabled with a reason, and enable authorized/available actions. Target-specific evaluation and execution are implemented by the separate Resource
Actions contract below. Execution rechecks authorization and availability. Static
metadata continues to expose authorization only.

Individual definitions support ETag and If-None-Match (including weak validators and
`*`): a matching representation returns 304 without a body. The quoted ETag contains
the structural DefinitionHash plus a SHA-256 of the explicit response representation,
so permission or module-state changes invalidate a previously cached effective
response even when the structural hash is unchanged. Metadata uses
`Cache-Control: private, no-cache` and `Vary: Authorization`; authorization/state is
re-evaluated before returning 304. Discovery is also private/revalidated. This is
metadata coherence, not Angular source regeneration or a new cache store.

### OpenAPI and shared contract boundary

The existing native `/openapi/v1.json` document is extended from the frozen resource
registry. Generic dispatcher templates are omitted; concrete resource paths and only
declared operations are projected. No repository/entity reflection or handwritten
per-resource schemas are used. Metadata endpoints are also described with explicit
DTO shapes. Production currently has no ResourceDefinitions, so there are no concrete
Resource CRUD paths in production OpenAPI until a module contributes definitions.

Semantic types map to boolean, string, integer or exact JSON number with UUID/date/
time/date-time/base64 formats. Intrinsic and declared bounds, enum values, lengths,
patterns, required fields and nullability come from metadata. CREATE excludes read-only
inputs and does not require fields with defaults; PATCH excludes ID/read-only/immutable
inputs and has no required properties. Response records and pages have separate
schemas. Static OpenAPI declares structural support, not per-user authorization or
current module/tenant availability.

The native OpenAPI library represents `multipleOf` using CLR decimal. Exact steps
representable there use standard multipleOf; larger/finer steps are preserved as the
canonical string extension `x-erbas-exact-multiple-of` instead of being rounded.
The Metadata API remains the complete exact semantic authority, including decimal
precision/scale. Neither OpenAPI nor JSON storage choices redefine semantic types.

This Resource API is an implementation-specific .NET extension, outside the published
shared `erbas-contract v0.12.0`. Existing shared routes, envelopes and Bruno assertions
remain unchanged. Portable clients must not assume these endpoints exist on other
stacks. Promotion to the shared contract requires the cross-stack review and explicit
contract revision described by that repository's implementation-extension policy;
this task neither revises nor republishes it.

## Executable Resource Actions

RESOURCE-ACTIONS-001 adds semantic parameters in Resources, typed orchestration in
Resources.Runtime and HTTP/OpenAPI projections in Resources.AspNetCore. Core and
Modularity have no outward action dependency. There are no production WMS action
adapters yet; test-only invoice/item/order examples prove the boundary.

`ActionDefinition` keeps its name, ENTITY/COLLECTION/SELECTION scope, permission,
RequiresSelection and annotations, adding a read-only Parameters list. Each
`ActionParameterDefinition` contains name, scalar type, nullable/required flags,
default, typed constraints, enum values and annotations. It has no persisted-field
capabilities, read-only or immutable flags. The existing scalar/constraint/default
validator is reused internally; invalid parameter definitions fail at registration.
`AnnotationTarget.ActionParameter` deliberately extends vocabulary targeting;
Common.Label is supported and custom typed vocabulary terms can opt into that target.

`ResourceActionInput` snapshots an ordinal read-only map of ScalarValue values. No
object, dynamic or JSON DOM crosses into Runtime. Unknown parameters, incompatible
values, constraints, enums and nullability are validated; required values must exist.
Defaults apply only to absent parameters. Explicit null remains null and never asks
for a default. No input is coerced. These parameter definitions also drive metadata
and OpenAPI; there is no separate HTTP action model.

Targets are typed `ResourceEntityTarget` (exactly one ResourceIdentifier),
`ResourceCollectionTarget` (no identifier) and `ResourceSelectionTarget` (snapshot of
explicit identifiers in caller order). IDs follow the resource's declared semantic
ID type. Selection is nonempty, duplicate-free and bounded to **100** IDs, sharing
the ResourceQuery maximum page-size constant. There is no all-matching-query target.

### Composition, scope and execution

Each `IResourceActionHandler` identifies exactly one registered resource/action and
receives a neutral execution context with its definition, target and validated input.
Ordinary scoped DI supplies module/domain services. `ResourceActionRegistry` captures
handlers and optional availability providers, rejects unknown/duplicate composition,
and checks captured identities again at dispatch. The Host resolves a validation scope
at startup, before serving HTTP. Constructors must remain free of database/domain
side effects, as with repository adapters. There is no mutation API or service locator.

Metadata-only actions remain legitimate. Executing an unbound action fails closed
with ActionHandlerNotRegistered; authorized availability for it also fails rather
than claiming it can execute. Handler presence is not a grant of permission.

Actions reuse `ResourceRepositoryCatalog` as fixed per-resource execution-scope
metadata, without requiring an IResourceRepository instance. Tenant/Platform/None
module declarations determine scope as for CRUD. Both modules require an explicit
existing ResourceRepositoryBinding for that resource; absence/contradictory/invalid
scope fails composition for registered handlers/providers. Scope never comes from
payload, annotations, current tenant presence or action name.

Execution follows:

```text
resource and registered action lookup
    ↓
module enabled?
    ↓
fixed execution scope; authoritative IRequestTenantContext when Tenant is required
    ↓
action's metadata permission through existing IResourceAuthorization
    ↓
matching typed target and bounded identifier validation
    ↓
parameter validation and defaults
    ↓
current business availability
    ↓
handler resolution and execution
    ↓
validated neutral outcome
```

Actions do not require public GET, UPDATE or other unrelated CRUD permissions.
The handler/domain owns entity/selection lookup and returns typed NotFound when
appropriate. There are no mandatory repository reads or generic N+1 existence checks.
Selection handlers must validate targets and enforce all-or-nothing business mutation
inside their own atomic boundary; generic orchestration never partially iterates them.

Tenant and permission authority remains scoped infrastructure, never caller input.
Zero memberships deny tenant actions; exactly one active membership/tenant establishes
context; multiple memberships retain `tenant_selection_required`. Revocation is
rechecked on later requests. Platform actions require no tenant membership. No tenant
selector, user/permission selector or database selector is introduced.

### Availability and outcomes

`IResourceActionAvailabilityProvider` optionally evaluates one resource/action's
neutral target context. It does not authorize and receives no arbitrary input JSON.
Without a provider, an otherwise executable authorized valid target defaults to
available. Availability is target-specific, not parameter-dependent in this first
contract; parameter-dependent business acceptance remains handler-owned.

`ResourceActionAvailability` contains Available and optional ReasonCode. Reasons must
be lowercase machine codes with dot/underscore/hyphen separators, bounded to 100
characters; available results must have no reason. Malformed results and unexpected
provider exceptions fail safely. Unauthorized actions return authorized=false,
available=false and no reason, without calling a provider or handler. Availability
batches return only actions of the requested scope in ordinal name order.

Execution always rechecks module/tenant/permission and evaluates availability again.
Metadata/availability responses are advisory frontend state, never authorization.
State or permission changes between discovery and click can therefore deny execution.

Handlers return only Success, NotFound, or Conflict with a validated stable reason
code. Success has no generic payload. Unexpected handler/provider failures become
safe ExecutionFailed errors; exception messages are not exposed. Handlers own final
transactional invariants, target existence and atomic persistence. Availability is
not a transaction guarantee; concurrent domain changes must still be checked by the
handler inside its transaction. Runtime cannot roll back effects already committed.
No generic transaction coordinator, locking framework or optimistic-lock engine exists.

Current audit seams remain HTTP/capability-specific. Generic domain/action audit
integration is deferred; no synthetic capability events or new audit framework is added.

### HTTP contract and Core FE

Execution is `POST /api/resource-actions/{resource}/{action}/execute`:

```json
{"target":{"id":"035a2161-c936-42dc-8b4b-d3cf5d1953a6"},"parameters":{"reason":"Approved"}}
```

For COLLECTION, omit target entirely. For SELECTION, use target.ids containing typed
ID values. Parameters may be omitted when no required input remains after defaults.
The `/execute` suffix deliberately avoids collision between the batch `/availability`
endpoint and a legitimate action named `availability`. CRUD routes remain unchanged.
Scope mismatches, extra authority fields, duplicate keys, query parameters and malformed
transport are rejected. Scalar JSON mapping/body limits are the existing Resource API
policy. Successful execution returns **204** with no body; Core FE refreshes affected
resource/list views.

`POST /api/resource-actions/{resource}/availability` accepts exactly a scope and the
corresponding target: `{"scope":"ENTITY","id":"..."}`,
`{"scope":"COLLECTION"}`, or `{"scope":"SELECTION","ids":["..."]}`.
It returns states such as:

```json
[
  {"name":"invoice.post","authorized":true,"available":false,"unavailableReason":"invoice_already_posted"},
  {"name":"invoice.cancel","authorized":false,"available":false}
]
```

Both endpoints use existing Bearer/current-user enforcement and no-store responses.
Existing ContractError maps unknown resource/action/target-not-found to 404, malformed
input to 400, forbidden/tenancy to existing 403 codes, module disabled to existing
503 module_disabled, missing handler to 503 resource_action_handler_not_registered,
unavailable to 409 resource_action_unavailable and business conflict to 409
resource_action_conflict. Stable reason codes use errors[].code; no human-language
message property is added. Unexpected provider/handler failures return safe 500
resource_execution_failed.

Metadata now includes parameter names/types/flags/defaults/constraints/enums/annotations.
It retains action.authorized but never contextual available. OpenAPI derives concrete
execution and availability paths, scope-specific target shapes and parameter schemas
from these same frozen definitions, including required/default/nullability and exact
numeric constraints. The existing exact multipleOf extension policy applies. This
remains a .NET implementation extension outside shared erbas-contract v0.12.0.

> Authorization controls visibility. Availability controls enabled state.

Future Core FE discovers definitions/parameters via Metadata API, normally hides
unauthorized actions, requests availability for the opened/selected target, disables
unavailable actions with a localized explanation mapped from the stable reason, and
enables available actions. Clicking opens a generic parameter dialog when needed,
executes the action, then refreshes affected views. Backend checks all authority and
availability again. No Angular component or frontend rendering is implemented here.

Deferred action capabilities include arbitrary response payloads, reports/downloads,
background jobs, command queues, query-wide bulk actions, action chaining, workflow/
state-machine/rules engines, scripting and generic compensations. AI/tool derivation,
frontend buttons/dialogs/themes, relation expansion and generic EF/schema generation
remain separate work.

## Presentation-neutral form metadata

RESOURCE-UI-METADATA-001 adds optional `ResourceDefinition.Presentation` in the semantic
Resources project, independent of HTTP, ORM, WMS/AI and frontend frameworks. It is an
explicit immutable hierarchy, not object-valued annotation JSON:

```text
ResourceDefinition
  → ResourcePresentationDefinition
    → ResourceFormDefinition
      → ResourceFormTab
        → ResourceFormSection
          → ResourceFormFieldGroup
            → existing field names
```

The form contains behavior, ordered tabs and optional action references. Tabs contain
ordered sections, sections contain ordered groups, and groups contain ordered field
references. All collections are defensive read-only snapshots. Tabs must be nonempty;
every tab/section/group must contain its next level. Tabs have unique technical IDs
within the form; section IDs are unique within their tab, and group IDs within their
section. IDs follow the existing technical field-name syntax and are bounded to 100
characters. Fully qualified parent paths disambiguate repeated child IDs.

Labels are optional literal plain Unicode text, bounded to 200 UTF-16 code units,
without markup angle brackets or control characters. They are not HTML, Markdown,
templates, localization keys or component names; renderers must treat them as text.
This follows the existing literal-label convention without adding a metadata i18n
framework. Sections may be collapsible/initiallyCollapsed; a non-collapsible section
cannot start collapsed. No grid, dimensions, responsive breakpoints, CSS, icons,
colors, numeric tabindex or component-library choices are encoded.

### Complete placement and action context

An explicit form must place every non-hidden, non-excluded resource field exactly
once, including read-only/immutable identifiers and audit fields. Unknown fields,
duplicates within/across groups and accidentally omitted visible fields fail startup
with InvalidPresentation. This deliberately prevents newly added visible fields
from silently disappearing. Resources without explicit presentation remain valid.

Existing `UI.Hidden=true` exempts a field from placement. New typed boolean
`UI.FormExcluded=true`, valid only on fields, explicitly excludes it from shared
form/detail presentation while leaving other uses such as list metadata unaffected.
False values do not exempt placement. Hidden/excluded fields cannot be placed in an
explicit form. Exclusion is presentation intent, not authorization or a restriction
on backend input/output; security remains authoritative independently.

Form, tab and section action lists reference existing ActionDefinition names. An
action can appear only once across the form; unknown/duplicate references fail startup.
Unplaced actions remain valid for the future default resource action area. Reference
order is intentional button order. Placement is visual context only: authorization
controls visibility, availability controls enabled state, and Resource Actions still
re-checks all guards and executes the same registered handler. No permission is
conferred by placement and no action must be placed to execute.

One hierarchy serves create, update and read-only detail. Existing required, nullable,
readOnly, immutable, defaults and constraints determine behavior for each operation;
there are no separate near-identical trees or new per-operation field flags. Read-only
fields may be displayed without being sent in mutations. Layout does not redefine
semantic fields, validation or persistence.

### Unsaved changes

ResourceFormBehavior exposes one UnsavedChangesPolicy: ConfirmDiscard by default,
or explicit AllowDiscard. The Metadata API uses CONFIRM_DISCARD / ALLOW_DISCARD.
Editable forms must track dirty state; read-only detail views are naturally non-dirty.
Under ConfirmDiscard, internal navigation away from unsaved edits must offer Save,
Discard/cancel editing, or Remain. Save must first pass normal form validation.
Protection covers another resource/record, menu navigation, closing edit panels/dialogs
and browser Back where the Angular runtime can intercept it. Switching tabs within
the same form does not discard the form. Explicit AllowDiscard permits leaving without
that confirmation. No redundant dirty-tracking booleans are added.

For refresh/tab close, the future runtime uses browser-supported beforeunload behavior
where available, subject to browser restrictions and native prompt limitations; it
cannot promise the internal three-choice dialog there. This ticket implements no
frontend state tracking, navigation guards or browser behavior.

### Immediate validation and cross-tab errors

```text
Metadata constraints
    ↓
Core FE immediate validation
    ↓
backend authoritative validation
```

Before POST/PATCH, future Core FE must validate every representable constraint:
required/nullability, semantic type, enums, numeric minimum/maximum and exclusive
bounds, multipleOf, exact decimal precision/scale, minimumLength, maximumLength and
pattern. It must preserve exact-number semantics and the documented Unicode/pattern
dialect, rather than substitute JavaScript floating point or incompatible regexes.
Frontend validation improves UX; backend validation remains mandatory and authoritative.
Manipulated requests must still fail backend checks.

Preventive controls should offer valid enum choices and appropriate semantic controls
for dates, numbers and booleans. For STRING/TEXT maximumLength, Core FE should prevent
normal typing and paste from producing longer values where the control permits this
cleanly, matching the semantic Unicode-scalar length rule. Backend maximumLength
validation still applies. Optional character counters belong to frontend/theme code;
there is no counter metadata flag.

Reactive validation remains necessary for required/minimumLength/pattern/range errors
and intermediate editing states. The frontend must never rely exclusively on
preventive controls.

> When Save is requested, the Core FE resolves errors in the active tab first. If the active tab has no errors but another tab does, it activates the first invalid tab in layout order, reveals the relevant container and focuses the first invalid field.

Save validates the complete applicable form before submission. If the active tab has
errors, remain there and reveal/focus its first invalid field. Otherwise activate the
first invalid tab; submit only if no errors remain. Reveal any collapsed section
containing the target field, then scroll/focus it. Tab error state/count is derived
from validation; visual indicators belong to the theme. No icons/colors are prescribed.
Deterministic error/focus order is tab → section → group → field order, never the order
in which validators happen to return issues. No separate reveal flag or tabindex is
needed.

### Metadata, cache coherence and acceptance

Individual metadata explicitly projects `presentation.form` with behavior, actions
and tabs, each tab's id/optional label/actions/sections, each section's id/optional
label/collapsibility/actions/groups, and each group's id/optional label/field names.
Absent presentation is omitted. Compact discovery does not include the layout.
The existing effective ETag incorporates the new projection and v3 DefinitionHash;
layout changes invalidate it without a second cache key or Angular regeneration.

Presentation affects Metadata API schemas only. CRUD/action OpenAPI request/response
schemas remain semantic field/parameter projections and contain no tabs or groups.
Runtime never interprets layout. The same definition continues through existing
module, tenant, authorization, CRUD and action guards.

A shared test-only item-style fixture exercises General, Logistics, Inventory,
Configuration and Audit tabs, ten sections, twenty groups and thirty placed fields,
plus hidden/excluded fields. It includes exact numerics, text, booleans, enums,
UUID/self-relation, dates/time/instants, required/nullability/defaults, read-only and
immutable fields, bounds, lengths and pattern, and form/tab/section action placement.
It is no production WMS adoption. WMS remains the real acceptance domain; a future
complex WMS maintenance screen must validate these contracts in the Angular runtime.

Conditional visibility/required expressions, nested layout languages, formulas,
workflow rules, relation expansion, user-saved layouts, table personalization, themes,
branding, localization infrastructure, frontend generation and AI behavior remain
out of scope. No Angular/client, frontend validation or WMS production migration is
implemented here.

## Authority and business rules

Backend metadata will remain authoritative. Frontend validation is convenience only;
direct Resource REST calls receive the same Runtime backend validation. Declarative
constraints do not replace contextual domain rules: discount UINT8 0..100 is metadata,
but salesperson/director discount limits belong to domain services, business validators,
action handlers or module-specific code. There is no business-rule scripting language.

## Core FE Resource Runtime

The primary frontend architecture is a Core FE Resource Runtime driven directly by
Resource Metadata. Angular is the planned initial frontend runtime implementation;
resource semantics and neutral layout remain independent of Angular and any chosen
UI component library.

> A standard functional module should not need its own frontend to expose CRUD,
> relations, filters, declarative actions and forms that can be represented through
> Resource Metadata.

> Changes representable purely through Resource Metadata should not require
> modifying or regenerating Angular code.

The target end-to-end architecture is:

```text
ResourceDefinition
    ↓
Resource Runtime
    ↓
Metadata API
    ↓
Core FE
    ↓
Angular Resource Runtime
    ↓
rendered CRUD/UI
```

The backend Resource Runtime owns generic execution. Core FE owns reusable frontend
runtime orchestration and rendering, with components equivalent to:

```text
MetadataClient
ResourceRuntime
ResourceList
ResourceForm
ResourceDetail
ResourceFilters
ResourceRelations
ResourceActions
LayoutRenderer
ThemeResolver
OverrideResolver
```

A module-specific frontend is reserved for exceptional requirements that cannot
reasonably be represented by the generic Resource Runtime. Standard resource
maintenance uses the shared Core FE components.

### Runtime and optional generation

```text
Primary:         metadata → Core FE Resource Runtime → UI
Optional future: metadata → code generator → specialized/generated source
```

Angular source generation is no longer the primary path. A generator may later
support specialized implementations; normal CRUD operation must not depend on it.
Metadata-only changes flow through metadata retrieval and runtime rendering without
Angular source modification or regeneration.

### Neutral composable layout

The implemented [form metadata](#presentation-neutral-form-metadata) supplies typed
ordered tabs, sections, field groups and references, with shared create/update/detail
behavior. It is independent of annotation JSON and component libraries. Richer layout
concepts such as headers or additional nesting remain future contract evolution;
rendering and responsive choices belong to Core FE/theme work.

### WMS acceptance domain

The existing WMS module is the primary real application acceptance domain for the
future ERBAS/Mosaiko Resource Platform and Core FE Resource Runtime. Its real
requirements must validate whether Resource Metadata and runtime rendering are
expressive enough. Relevant existing resources include:

- Warehouse.
- Location.
- Item (the existing `StockItem` domain model).
- InventoryMovement.
- Inventory (the existing `InventoryBalance` query projection).

See [the current WMS inventory foundation](wms-inventory.md) for the implemented
operations, relationships, constraints and permissions. Standard declarative WMS
interfaces should be representable through Resource Metadata without resource-specific
Angular code. Use real WMS requirements to evaluate:

- Complex forms, multiple sections/groups and nested/sectioned layouts.
- Relations between resources.
- Create versus update behavior and read-only resources/views.
- Filtering, searching and sorting.
- Permissions and declarative actions.
- Contextual field behavior where representable declaratively.

Acceptance must preserve each resource's actual supported operations and semantics.
The current warehouse/location/item masters and movements expose creation and reads;
movements remain append-only, and Inventory is a read-only projection. Create/update
distinctions must reflect where updates are actually supported. Filtering and ordering
must preserve the documented behavior. Broader search, actions and contextual/layout
behavior are evaluation dimensions to validate against actual WMS requirements,
not claims that every listed resource already implements them or authorization to
invent functionality for a showcase.

> Do not simplify WMS requirements to fit the generic Resource Runtime. The Resource
> Platform must be expressive enough to support normal WMS requirements or provide a
> clean partial override mechanism when a requirement is genuinely non-declarative.

WMS is an acceptance domain, not a dependency of `Alxarafe.Resources`:

```text
Alxarafe.Resources        → must NOT depend on WMS
WMS                      → may later contribute ResourceDefinitions
Core FE Resource Runtime → consumes generic Resource Metadata
```

The target architecture separates responsibilities explicitly:

| Boundary | Responsibility |
| --- | --- |
| ResourceDefinition | Semantic definition and neutral layout |
| Resource Runtime | Behavior |
| Repository adapter | Persistence |
| Core FE | Rendering |
| Theme | Presentation |

### Partial overrides

The intended frontend override progression is:

1. Standard metadata.
2. Metadata override/extension.
3. Custom renderer.
4. Custom component.
5. Fully custom frontend.

This refines the progression from metadata and automatic CRUD through metadata
extension and partial custom code to full custom implementation. If a future WMS
screen contains behavior that cannot reasonably be represented declaratively, only
that part should require custom frontend. For a future Warehouse screen needing an
exceptional visualization, the conceptual split could be:

```text
Warehouse

LIST       generic Resource Runtime
FORM       generic Resource Runtime
DETAIL     generic Resource Runtime
MAP        custom component
```

A custom component must not force replacement of the whole resource UI; generic
behavior remains available for the other views and CRUD operations.

Overrides preserve backend validation and authorization. Exceptional UI behavior
can replace a renderer/component without requiring a replacement resource frontend.

### Themes

```text
Resource Runtime → decides WHAT must be rendered
Theme            → decides HOW it is rendered
```

The same ResourceDefinition must remain usable across visual themes. Theme and
presentation choices belong to the frontend rendering boundary; resource metadata
must remain independent of the Angular/UI framework selected to implement it.

### Long-term success criteria

1. A standard business maintenance screen supports list, create, update, detail,
   filtering, searching, ordering, relations, declarative validation, permissions,
   actions and layout without resource-specific Angular code.
2. A change representable in metadata requires no change to Core FE or the module
   frontend.
3. Adding a standard resource requires no manual registration of Angular components
   for that resource.
4. The same ResourceDefinition works independently of the visual theme.
5. Exceptional UI behavior can be replaced partially without losing generic behavior
   for the remaining CRUD.
6. A meaningful set of existing WMS maintenance screens must be implementable through
   Resource Metadata and the Core FE Resource Runtime with little or no
   resource-specific Angular code.
7. Resource Runtime design must be validated against real WMS requirements before
   being considered mature.

## Future phases

```text
RESOURCE-METADATA-001 [implemented]
    semantic model
    ↓
RESOURCE-RUNTIME-001 [implemented]
    generic backend execution
    ↓
RESOURCE-API-001 [implemented]
    REST + Metadata API + OpenAPI
    ↓
RESOURCE-ACTIONS-001 [implemented]
    action availability and execution
    ↓
RESOURCE-UI-METADATA-001 [implemented]
    neutral form layout and behavior
    ↓
ANGULAR-RESOURCE-RUNTIME-001
    universal Core FE renderer
    ↓
WMS complex acceptance/adoption
    ↓
RESOURCE-OVERRIDES-001
    metadata/custom component overrides
    ↓
THEME-RUNTIME-001
    presentation/theme resolution

Parallel/later Core FE work:
    CORE-FE-EXTENSIONS-001 → AI-FE-ASSISTANT-001

Later, optionally:
    Angular code generation
    AI/tool schema derivation
```

RESOURCE-METADATA-001 implements semantic metadata, definition validation, the registry
and DefinitionHash. RESOURCE-RUNTIME-001 implements generic backend CRUD through ports,
authoritative guards, runtime validation and business hooks. RESOURCE-API-001 implements
HTTP CRUD, explicit metadata projections/coherence and derived OpenAPI. RESOURCE-ACTIONS-001
adds typed action inputs, authorization, target availability and handler execution.
RESOURCE-UI-METADATA-001 adds typed form presentation and neutral unsaved-change policy.
Frontend runtime, layout rendering, overrides and theme resolution
remain future phases. No generic EF/SQL repository, schema generation, automatic migrations,
Angular/client code, relation expansion, reseller/branding or AI/tool schemas are added.
Existing WMS APIs/persistence are not migrated or redesigned. Core FE shell extensions and
the AI floating assistant remain separate work, not Resource Metadata functionality.
