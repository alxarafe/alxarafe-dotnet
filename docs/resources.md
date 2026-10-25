# Semantic Resource Metadata

RESOURCE-METADATA-001 implements the first Resource Platform foundation in
`Alxarafe.Resources`. Backend metadata is the semantic authority; framework-specific
representations must be derived later. This is a metadata model, validator and
read-only registry, not CRUD or a low-code/business-rule engine.

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

## Authority, business rules and future phases

Backend metadata will remain authoritative. Frontend validation is convenience only;
direct REST calls must eventually receive the same backend validation. Declarative
constraints do not replace contextual domain rules: discount UINT8 0..100 is metadata,
but salesperson/director discount limits belong to domain services, business validators,
action handlers or module-specific code. There is no business-rule scripting language.

```text
RESOURCE-METADATA-001: semantic model, validation, catalog, hash
    ↓
RESOURCE-RUNTIME-001: ResourceRecord, repository port, validation pipeline, CRUD execution
    ↓
RESOURCE-API-001: REST, metadata API, derived OpenAPI
    ↓
ANGULAR-RESOURCE-001: metadata client, generated/, custom/, overrides, themes
    ↓ later
AI/tool schema derivation
```

The intended customization progression remains:

```text
Metadata → Automatic CRUD → Metadata extension → Partial custom code → Full custom implementation
```

This phase adds no generic persistence repository/record, SQL/EF runtime, dynamic
schema generation, CRUD/metadata/action HTTP API, query execution, OpenAPI generation,
Angular/client changes or AI/tool generation. Existing WMS resources are not migrated.
