# Authoritative tenancy and database-per-tenant foundation

CORE-TENANT-001 implements the .NET backend foundation. ERBAS is multi-tenant at
application level; each Tenant owns an independent PostgreSQL business database.
It does not implement a reseller product, tenant selector, commercial provisioning
or business-data authorization through branding/domain names.

## Implemented boundary

```text
Control plane
  Platform DB: module states, tenants, tenant_memberships,
               tenant_ai_settings, ai_usage_invocations, ai_usage_records
  Security DB: existing Identity users, credentials and permission claims

Tenant A business DB: WMS catalogues/inventory and Assets resources
Tenant B business DB: independently migrated copies of those module schemas
```

Identity remains in its existing separate Security database, which is part of
the control plane. This task does not move existing authentication tables.
There is no separate Catalog DbContext in this repository: UoM, Storage Attribute
and Item Family catalogues are owned by WMS. `WmsDbContext` and the module-owned
`AssetsDbContext` are business contexts in each independent tenant database.
Assets owns six tables and its separate migration history; see [Assets](assets.md).
`PlatformDbContext`, `SecurityDbContext` and
`AiUsageDbContext` remain control-plane persistence.

Business tables have no tenant discriminator columns or shared-table tenant
filters. The same SKU, entity UUID and movement key can exist independently in
different business databases. Module availability remains the existing global
Platform state; no per-tenant module inheritance is implemented.

## Canonical Tenant and membership

Platform owns the canonical Tenant with:

- stable internal string ID (normal bootstrap uses a fixed synthetic UUID);
- ordinal, unique stable key;
- display name;
- active state;
- unique non-secret database reference.

Tenant IDs are bounded to 256 characters; keys and database references to 128,
and display names to 256. Metadata cannot be blank; database references are
restricted to letters, digits, underscore and hyphen, rather than raw connection
strings. There are no speculative commercial fields, hierarchy,
branding fields or timestamps; existing Platform entities have no timestamp
convention. Registry data is managed through controlled internal persistence,
not a new public administration API.

`tenant_memberships` stores user UUID, canonical tenant ID and active state, with
a unique composite primary key and tenant foreign key. Identity is in another
control-plane database, so there is no cross-database user foreign key. Every
resolution verifies that the authenticated user still exists and is enabled
through the existing authoritative security adapter. An orphan membership cannot
authenticate a deleted user or establish authority.

Membership only permits establishing a business execution context. It grants no
WMS, capability, primitive, module administration or AI permission. A CORE admin
without membership cannot read tenant business data. A member without the
existing permission also cannot read it. Production account creation does not
assign memberships automatically.

## Request-scoped authoritative context

The Host supplies scoped `IRequestTenantContext` / `ICurrentTenantContext` through
`TenantContext`. Its asynchronous `RequireAsync` resolves the authenticated,
persisted user and that user's persisted active memberships:

| Active memberships in active tenants | Result |
| --- | --- |
| Exactly one | Establish that canonical Tenant |
| Zero | Deny business execution explicitly |
| More than one | Deny: explicit tenant selection is required |

Inactive tenants cannot establish a business context. After persisted user
validation, middleware establishes the context for protected authenticated requests.
A missing/ambiguous context does not itself block control-plane operations or
tool-free AI; business persistence requires successful fresh resolution and then
propagates denial. Anonymous liveness never resolves tenant/security persistence.
The read-only TenantId is available after successful resolution; merely
constructing a context does not invent an identity. An established tenant is never
replaced by another tenant within a request. Tenant/database-reference changes
fail closed. Identity and memberships are rechecked before each business context
creation, so revocation cannot be bypassed by an earlier tool/request snapshot.

There is no selection from an arbitrary header, hostname, query string, user
UUID, email domain, username or development naming convention. No tenant header
or route parameter is added. Multiple-membership users cannot execute business
operations until a later explicit-selection capability exists.

Direct business endpoints retain their existing success shapes. New unavoidable
context failures use the existing `ContractError` shape with HTTP 403 and codes
`tenant_required`, `tenant_inactive`, `tenant_selection_required` or
`tenant_context_changed`. A missing/invalid/conflicting database mapping uses
HTTP 503 `tenant_database_unavailable`. These are implementation-specific tenancy
failures, not additions to the published shared OpenAPI. The unchanged shared
contract is exercised with an explicitly bootstrapped single-membership identity.
Existing capability/READ invocation denial semantics are preserved; no business
payload is returned after membership rejection.

## Central database resolution and secrets

All business persistence uses neutral Core `ITenantDatabaseResolver`, implemented
centrally by Host `TenantDatabaseResolver`. Business modules do not choose a
Tenant or inspect HTTP input. The resolver is a stateless singleton accessor;
it obtains the scoped authoritative context from the current HTTP request. It
retains no global current-tenant state. The singleton WMS context factory creates
fresh EF options and a DbContext from the connection resolved for each operation;
it does not cache one connection through singleton EF options.

The registry stores only a reference, for example `tenant_a`. Externally supplied
configuration/secrets map it through the existing .NET connection-string facility:

```text
Tenant.DatabaseReference = tenant_a
configuration key       = ConnectionStrings:Tenant_tenant_a
environment key         = ConnectionStrings__Tenant_tenant_a
```

Supply the actual connection through the installation's secret/configuration
mechanism. Do not store passwords or raw connection strings in Tenant rows,
settings, logs, reports or source-controlled examples. No credential management UI
or secret store is introduced. Provision dedicated database roles with access only
to their intended business database in a real installation; this task does not
provision production roles or databases.

Missing/invalid mappings and inactive/unknown tenants never fall back to another
business database or the old global `ConnectionStrings:Wms`. Runtime no longer
uses that global WMS setting. The resolver rejects mappings that identify a
Platform/Security database or reuse a configured tenant's PostgreSQL target
(host, port, database). References are unique in the registry. Database identity
must be configured consistently: alternate DNS/proxy aliases must not disguise a
shared physical database. Merely changing a search path is not tenant isolation.
A changed database target within an HTTP request fails closed. Only non-secret
target identity is retained in request items; resolved connection strings are
never logged. Invalid connection parser exceptions are replaced with a safe code.

## Migration and installation implications

Platform migration `202610260001_AuthoritativeTenancy` adds the registry and
memberships under `__PlatformMigrationsHistory`. AI usage remains under its
existing `__AiMigrationsHistory`. WMS retains its four unchanged migrations and
`__WmsMigrationsHistory`, independently in each tenant database.

The existing module startup initialization is retained. Neutral
`ITenantDatabaseMigrationRunner` enumerates registered active tenants and supplies
their centrally validated connections to each business module's existing migration
callback. The module opens the database **before** calling EF migrations: a missing
database fails rather than allowing EF to create it. Inactive tenants are skipped;
an active tenant with an invalid mapping fails startup clearly. With an empty
registry the control plane can start without a default business database.

### Current module-owned migration extension seam

WMS and Assets are business modules with persistence. Their
`WmsModule.InitializeAsync` and `AssetsModule.InitializeAsync` each supply a callback to
`ITenantDatabaseMigrationRunner`; each callback owns construction/configuration
of its module DbContext and its EF migrations. `ModuleRuntime` calls the optional `IModuleInitializer.InitializeAsync`
hook in validated dependency order without selecting a business module by name. The neutral runner enumerates tenant
targets and invokes the supplied callback; it contains no WMS branch, schema,
DbContext or migration-history knowledge. `ITenantDatabaseResolver` represents
canonical request context to tenant business database, not a WMS database.

A future persistent business module can supply its own initialization callback
through this existing composition seam without adding module-specific migration
logic to Core's tenancy resolver/runner. It uses the same independent business
database for a given Tenant, with its own module-owned schema/migration history;
this does not introduce one database per module. The Host's existing explicit
entry-assembly registration is composition, not knowledge of module business
semantics or a WMS-specific tenant provisioning path.

CORE-MODULE-001 formalizes the optional initializer and declares WMS persistence as
`Tenant`, while reusing this callback unchanged. The formal module descriptor does
not route connections or grant tenant access. Broader provisioning/lifecycle behavior
below remains future architecture; no generic migration engine is added. See
[the module contract](modularity.md).

This is not production provisioning, a distributed migration coordinator or an
online schema rollout platform. Operators must create databases/roles, supply
mappings, register tenants/memberships and run the established startup lifecycle
under appropriate maintenance control. Migration permissions should be separated
from normal runtime database roles in a production deployment design.

Existing installations that previously co-located WMS and Platform require an
operator-controlled business-data relocation before enabling tenant execution.
No data is silently copied, reassigned, dropped or inferred by this task. The
legacy tables must not remain the operational shared business store. Back up,
create the independent database, migrate its schema, transfer/verify the intended
business data, configure its reference and then establish explicit memberships.
The code never reads legacy Platform WMS tables as a fallback.

### Legacy tenant AI settings

AI is one module among others. Its `tenant_ai_settings` table was introduced by
AI-PROVIDER-002 before the canonical Tenant foundation. It is AI-specific
control-plane persistence, not the generic model for tenancy or module
configuration. Keeping it in Platform does not mean all module configuration
belongs there. The canonical FK alignment is transitional/evolutionary compatibility
work, not implementation of the future module configuration contract.

The original `tenant_ai_settings.tenant_id` remains the legacy external key to
preserve pre-authority rows and rollback compatibility. Nullable
`canonical_tenant_id` has a foreign key to Tenant and a unique index: one settings
row can link to one canonical Tenant, and one Tenant cannot have multiple linked
rows. Old rows remain unlinked; migrations do not manufacture tenants from them.
Only an explicit controlled mapping can establish the relationship. The read port
now queries canonical identity, never guesses from a matching legacy string.

AI-PROVIDER-002B activates the scoped AI resolver/bridge in production using only
this authoritative context and the canonical settings FK. Provider and model
independently inherit global defaults; later scopes see settings updates. Usage
summaries in Platform capture canonical tenant identity alongside existing user
attribution; attempts link to the same invocation. Unresolved contexts and historical
usage remain unattributed. Billing mode is not inferred. See [AI providers](ai-providers.md#tenant-overrides-active-at-runtime).
AI remains one module using tenancy, not a privileged Core concept. Generic module
configuration, Partner/reseller inheritance, BYOK, LOCAL_ONLY/CLOUD_ALLOWED,
data-processing/location policy, automatic routing/fallback and administrative UI
remain deferred.

## Development and deterministic validation

Normal `./bin/up` provisions only the fixed local synthetic business database
`alxarafe_business` through Docker's PostgreSQL service before starting the app.
The fresh-volume init script also declares that development database. Neither path
is production application provisioning. Existing local business data in the old
shared database is not moved automatically.

Compose explicitly enables `Tenancy:DevelopmentBootstrap`. It is honored only in
Development/Testing. It registers the synthetic development tenant and persists
memberships for controlled seed users and CORE-administrator-created development/test
users. Self-registration does not implicitly grant membership.
This is explicit bootstrap, not resolution by email or a missing-membership
fallback. Existing different/disabled registry state is not overwritten. Existing
memberships are not replaced. Production ignores the bootstrap flag. There is no
real customer seed in a migration. `SecuritySeed` and normal Identity hashing,
permission claims and controlled administrator safeguards remain in use.

`bin/validation-db` prepares four disposable databases: Platform, Security and two
independent business databases. `bin/test` and `bin/check` supply explicit mappings;
`bin/check` verifies that business tables/history are absent from Platform and
control-plane tables are absent from business storage. In Testing, the central
resolver rejects every business target outside the two fixed validation databases,
even if a registry reference/mapping is supplied. The workflow then runs the unchanged
published v0.12.0 conformance and local Bruno suites.

Existing native fixtures explicitly arrange data outside HTTP through a test-only
resolver. Every HTTP call delegates to the production membership resolver; there
is no production no-HTTP fallback. Isolation tests use real PostgreSQL, the same
SKU and UUIDs with quantities 100/900, concurrent HTTP requests, persisted
membership revocation, inactive/ambiguous/no memberships, permission denials,
missing/aliased/control-plane mappings and a missing-database migration check.
No live AI provider call is necessary.

## Designed but deliberately deferred

The following is future architecture, not implemented behavior or speculative
schema in CORE-TENANT-001.

### Future common module contract and lifecycle ownership

CORE-MODULE-001 now implements the minimum [module contract](modularity.md): identity,
release/API versions, dependency validation, read-only catalog, deterministic ordering,
persistence scope, presence of module-owned configuration and optional startup hooks.
The broader responsibilities below remain deferred.

Every ERBAS module will eventually publish richer requirements describing what
Core needs to install, activate, validate and provision that module for a tenant
without Core containing module-specific knowledge. Core orchestrates modules;
the module owns the meaning and implementation of its requirements.

Core will know:

- module identity and dependencies;
- whether the module is available/active;
- when lifecycle operations must run;
- tenant, context and configuration scopes.

Each module will own:

- its configuration definition and required versus optional settings;
- defaults and inheritance/override rules where supported;
- configuration validation;
- secret declarations;
- persistence requirements;
- tenant provisioning behavior;
- lifecycle-specific behavior.

Future conceptual lifecycle responsibilities may include equivalents of:

```text
install
upgrade
validate
activate
deactivate
provisionTenant
startTenant
removeTenant
```

These are future architectural responsibilities, not operations implemented by
CORE-MODULE-001. Its optional startup hook preserves current technical initialization;
it is not an install/upgrade/activation or tenant lifecycle engine. Richer configuration
definitions and lifecycle orchestration require separately approved work.

### Future configuration scopes and placement

Configuration is not AI-specific. Where a module supports inheritance, future
tenant/module configuration may follow:

```text
Platform defaults
  -> Partner inherited values
  -> descendant Partner overrides
  -> Tenant overrides
```

Each module defines its own semantics: which values are inheritable, required,
optional or overridable, how defaults combine, and how validation applies. Core
must not need to understand meanings such as `ai.provider`,
`wms.allowNegativeStock` or `accounting.fiscalYear`. The chain does not impose a
single merge algorithm or override rule on every module or setting, and it does
not turn branding's nearest-override rule or restrictive module availability into
a universal configuration policy.

Not all module configuration necessarily belongs in Platform DB. Control-plane
availability, activation and inheritance metadata may belong in Platform;
operational business configuration may belong in the Tenant DB. Secrets should
remain outside ordinary persisted configuration where appropriate, resolved through
the installation's secret/configuration boundary. Modules will declare their needs
and scopes; Core will orchestrate those declarations without interpreting business
values. No generic key/value configuration engine or new persistence model is
implemented by this foundation or audit.

### Partner and reseller administration

Partner is a commercial/administrative entity, not a business-data isolation unit.
Future Partners may manage tenants and descendant Partners:

```text
Partner A
  +-- Partner B
  |     +-- Tenant B1
  +-- Partner C
        +-- Tenant C1
```

No Partner table, hierarchy, multi-parent structure or reseller API exists yet.
Commercial/platform administration must never automatically grant access to ERP
business data. Membership plus business permissions remain a separate boundary.

### Restrictive module inheritance

Future module availability may only narrow down the hierarchy:

```text
effective modules(child) = effective modules(parent) intersection child allowed modules
```

A descendant may restrict/remove modules but cannot re-enable a module denied by
an ancestor. Future design must distinguish available modules, defaults enabled
and actually enabled state. None of that inheritance changes today's global
module registry/state behavior.

### Brand and white-label inheritance

Brand is separate from Tenant identity and Partner authorization. Future visual
configuration is inherited property by property:

```text
Platform -> Partner -> Subpartner -> Tenant
```

The nearest explicit override wins for product name, logo, favicon, colors, login
background, email templates, document/PDF branding and support/legal links.
Inherited assets should not be physically copied merely to represent inheritance.
There is no branding persistence, inheritance engine or UI in this task.

The intended logical asset namespace is:

```text
branding/
  platform/default/
  partners/<partner-key>/
  tenants/<tenant-key>/
```

Actual storage may be object storage rather than a local filesystem. No file
storage service or asset copying mechanism is implemented now.

### Custom domains and explicit tenant selection

Future custom domains are explicit bindings, not blindly inherited Brand
properties. A hostname may locate a Brand or requested context but must never
by itself authorize access to a tenant database.

Users with multiple memberships will eventually require explicit tenant selection.
That future requested context must be checked against persisted membership and
tenant state. A selector UI, requested-context transport and its session/token
semantics require a separate approved design. They are deliberately absent now;
the present runtime rejects ambiguous membership instead of choosing arbitrarily.
