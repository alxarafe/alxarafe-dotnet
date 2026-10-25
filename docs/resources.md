# Semantic Resource Metadata

RESOURCE-METADATA-001 implements the first Resource Platform foundation in
`Alxarafe.Resources`. Backend metadata is the semantic authority; framework-specific
representations must be derived later. This is a metadata model, validator and
read-only registry. RESOURCE-RUNTIME-001 adds generic backend execution in the separate
`Alxarafe.Resources.Runtime` boundary; it is not a low-code/business-rule engine.

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
has an empty resource catalog. There is no metadata or administration endpoint.

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
not CLR `decimal`. `ResourceNumber` uses arbitrary-precision integer coefficient and
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
to STRING/TEXT; filterable/sortable are declarations with no execution in this phase.

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
with no execution handlers or HTTP routes here.

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
UTF-8 JSON semantic projection marked `erbas.resource-definition.v1`. It includes
identity/version/owner/ID field, fields and effective constraints/defaults/capabilities,
relations, operations/security, actions and typed annotations (including used term
shapes/targets). It includes no CLR type/assembly name, provider implementation,
object hash code, DI order or runtime state.

Object property order is fixed. Fields, relations and actions are sorted by technical
name; operations use declared semantic enum order; permission sets, enum values and
annotation keys use ordinal order. Numeric spelling and equivalent discrete bounds
are normalized; UUIDs use canonical form, instants UTC and binary canonical base64.
Explicit annotation field-name list order remains significant. Text and regex
expressions retain their declared meaning; the hash is not a theorem prover for
arbitrarily equivalent patterns. Relation targets are hashed by referenced identity
and field names, not recursively: changing a target definition changes its own hash.
Consumers needing a whole catalog fingerprint must combine the relevant resource
hashes in a future, explicit dependency policy.

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
invalidation and this coherence behavior are not implemented in this phase.

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
checks identity again before dispatch. Repositories may not change identity after
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
databases to prove isolation without new HTTP endpoints or a second tenant selector.

## Authority and business rules

Backend metadata will remain authoritative. Frontend validation is convenience only;
direct REST calls must eventually receive the same backend validation. Declarative
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

Resource Metadata must eventually support a composable, presentation-neutral layout
model capable of expressing structures equivalent to:

```text
Layout
├── Header
├── TabGroup
│   ├── Tab
│   │   ├── Section
│   │   │   ├── Field
│   │   │   └── Section
│   │   └── Section
│   └── Tab
└── Actions
```

The current annotation/vocabulary architecture must remain extensible enough to
support typed layout terms and nested structures in a later phase. Exact layout
types, schemas and renderers are outside RESOURCE-METADATA-001 and are not implemented
here. Layout describes composition and intent; it must not encode `MatInput`,
`PrimeNG`, Bootstrap classes, Tailwind classes or CSS classes.

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
RESOURCE-API-001
    REST + Metadata API + OpenAPI
    ↓
ANGULAR-RESOURCE-RUNTIME-001
    universal Core FE renderer
    ↓
RESOURCE-OVERRIDES-001
    metadata/custom component overrides
    ↓
THEME-RUNTIME-001
    presentation/theme resolution

Later, optionally:
    Angular code generation
    AI/tool schema derivation
```

RESOURCE-METADATA-001 implements semantic metadata, definition validation, the registry
and DefinitionHash. RESOURCE-RUNTIME-001 implements generic backend CRUD through ports,
authoritative guards, runtime validation and business hooks. Resource-API, frontend
runtime, composable layout execution, metadata cache coherence, overrides and theme
resolution remain future phases. No generic EF/SQL repository, schema generation,
automatic migrations, CRUD/metadata/action HTTP API, OpenAPI generation, Angular/client
code, action execution, relation expansion, reseller/branding or AI/tool schemas are
added. Existing WMS APIs/persistence are not migrated or redesigned.
