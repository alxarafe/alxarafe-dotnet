# Module extension model

## Architectural requirement

ERBAS must support future third-party modules without modifying or recompiling
its CORE. Production selects one implementation: Java or .NET. Java plugins may
be distributed as Java JARs/packages and .NET plugins as assemblies/packages.
They share modular semantics, not binary compatibility or a cross-runtime RPC
protocol. Physical external loading is not implemented in this task.

Built-in modules and future third-party modules use the same pipeline:

```text
ModuleSource(s) -> ModuleRegistry -> ModuleGraph -> ModuleRuntime
```

## Descriptor, version and availability

`ModuleDescriptor` is immutable and contains only an ID, Module API version and
an immutable dependency ID list. IDs are stable, opaque, case-sensitive and
identify modules persistently. They are chosen by the module owner, independently
of class names, packages, assembly names or package filenames. Third parties are
responsible for sufficiently unique IDs. Descriptors must remain stable throughout
startup and are the sole source of identity, version and dependency metadata.

The initial Module API version is 1, centralized in the platform's Module API
constant. Registration supports only that current version. An incompatible version
fails with the module ID and declared version before service configuration or
initialization. There is no negotiation, multi-version compatibility or version
range resolution.

A source supplies modules for the current startup. The framework-independent
registry gathers all sources, rejects duplicate IDs within or across sources,
validates API versions and exposes a read-only snapshot in ordinal/natural ID
order. Presence in this registry is the authority for **AVAILABLE**: discovered
by a valid source during this startup. Availability is not persisted. A future
package source could make a module available when its package is present and
absent after removal and restart without changing the registry or graph.

The graph consumes all available modules, validates missing dependencies and
cycles, and traverses root IDs and dependency IDs in ordinal/natural,
case-sensitive order. Dependencies precede consumers; every module is included
once. The topological result is deterministic and read-only. Dependencies can
cross source boundaries. Zero sources or modules are valid.

## Technical initialization and CORE boundaries

All AVAILABLE modules initialize sequentially in resolved dependency order.
Failures propagate and stop subsequent initialization. There is no rollback,
retry or parallel initialization. **INITIALIZED does not mean ENABLED**:
initialization remains technical even when the persistent enabled flag is false.

The CORE is not a disableable functional module. Basic Health, essential
authentication, the modular runtime and essential system administration remain
outside functional module activation. AUTH/USERS behavior is unchanged.

Modules must not access another module's internal tables or private repositories.
Future integration must use public contracts/capabilities of the owning module;
the AI assistant already does so through CORE, and future Inventory integrations
must follow the same boundary. CORE owns the capability registry, authenticated
discovery and synchronous READ invocation; in-process module executors own
capability business behavior. No capability bus is introduced.

## Persistent dynamic state

**ENABLED** is administrative state stored in PostgreSQL, independently of
AVAILABLE and INITIALIZED. **OPERATIONAL** is not modeled. State belongs to the
platform/CORE, never to Security or a business module.

The platform table `module_states` has exactly two columns: `module_id TEXT
COLLATE "C" PRIMARY KEY` and `enabled BOOLEAN NOT NULL`. IDs retain the descriptor's
case-sensitive semantics and are not constrained by class or package names.
There are no module foreign keys, timestamps or configuration fields.

After registry/graph validation, startup synchronizes state before technical
initialization. Missing rows are inserted with the centralized initial policy
`enabled = true` to preserve existing behavior. Existing flags are never rewritten.
Rows for unavailable modules remain untouched: removing a package and restarting
removes availability, but reinstalling the same ID recovers its previous state.
An enabled module whose dependency is disabled makes startup fail; synchronization
rolls back rather than repairing persisted choices or leaving partial inserts.
Zero available modules are valid and do not remove retained state.

`ModuleStateService` reads the database for every enabled check; it keeps no
persistent enabled-state cache. A committed change is immediately visible to a
new check, including from another service instance sharing this database.
Availability is still checked against the current registry, never inferred from
a retained database row. Unknown/unavailable IDs cannot be queried, enabled,
disabled or passed to `requireEnabled` (named `RequireEnabledAsync` in .NET).

Enable requires all declared dependencies to be enabled. Disable fails while an
enabled module depends on the target. There are no automatic cascades: disable
consumers before dependencies; enable dependencies before consumers. Validating
every enabled module's direct edges also preserves the invariant through indirect
dependency chains. Repeating an already satisfied transition succeeds without
rewriting unchanged rows. Failures identify the module and offending dependency.

Synchronization and transitions use a READ COMMITTED transaction and acquire
`LOCK TABLE module_states IN SHARE ROW EXCLUSIVE MODE` before reading the snapshot.
One lock serializes modifications, including initial insertions into an empty
table, without a multi-row lock order or permanent memory authority. Validation
precedes writes; exceptions roll back the whole operation. This deliberately
simple policy fits a small module set and coordinates separate application
instances. Ordinary checks continue to read committed database state.

Disabling never deletes module data, tables or configuration, reverts migrations,
unloads code, or changes technical initialization. All available modules still
initialize, including disabled ones. `requireEnabled` provides the shared internal
guard used by the platform HTTP gate. CORE module administration is outside that
gate; INITIALIZED never substitutes for permission to execute functional work.

## CORE module administration

Published ERBAS Contract `v0.8.0` defines `GET /api/modules` and
`PATCH /api/modules/{id}`. The Host maps them as Minimal APIs, following the
existing Auth/User endpoint pattern. They are outside the functional module group
and never return `module_disabled`. Existing bearer authentication, current
Identity state and the same CORE administrator policy apply before body/query
parsing, registry lookup or state mutation. No new RBAC is introduced.

The closed Module contains exactly `id`, `moduleApiVersion`, `dependencies`
and `enabled`. Metadata comes from descriptors in the current registry;
dependencies are sorted ordinally and enabled state is read from persistence
for every response. Historical rows remain retained but do not imply availability.
Registry IDs use ordinal case-sensitive order before pagination. The small
`CollectionPagination` parser, fixed order metadata and arbitrary-size offset
converter are shared with Users; its existing wire behavior is preserved.
The published [OpenAPI](https://github.com/alxarafe/erbas-contract/blob/v0.8.0/openapi/erbas.yaml)
owns the closed collection envelope and pagination rules.

PATCH accepts exactly one required Boolean `enabled`, with exact property
casing and no duplicates, extra fields, nulls or scalar coercion. JSON is parsed
inside the authorized delegate. Success, including an idempotent transition,
returns 200 with the complete current Module. There are no cascades.
`ModuleUnavailableException` and `ModuleDependencyConflictException` identify
only the contractual 404/409 outcomes; missing state and unexpected storage errors
remain internal failures. No exception-message parsing or broad error conversion
is used. The unchanged EF transaction and table lock still validate the snapshot
before any writes, so a conflict persists no partial mutation.

Specified responses are JSON/no-store. Errors contain only `code`: 400
`invalid_request`, 401 `unauthorized`, 403 `forbidden`, 404
`module_not_found`, 409 `module_dependency_conflict`. Only 401 carries
`WWW-Authenticate: Bearer`; no dependency IDs or internal diagnostics are exposed.

### Explicit conformance composition

Normal composition includes erbas.wms. Only environment
`Testing` together with `Modules:ConformanceEnabled=true`
(environment binding `Modules__ConformanceEnabled`) adds a
`ConformanceModuleSource`. The flag defaults to false. Development/Production
do not activate the source even if the flag is supplied.

Validation Compose explicitly enables it. The source supplies exactly
`erbas.dev.math` without dependencies and `erbas.dev.statistics` depending on
Math, both using `ModuleApi.CurrentVersion`. IDs and configuration/environment
names are centralized in that source. Descriptors have no special environment
field, and these technical modules have no business or mathematical functionality
and no functional HTTP endpoints.

All sources enter one `ModuleBuilder` before its single `Build()`, yielding
one registry/graph/runtime per application. Normal synchronization creates missing
canonical rows as enabled, preserving existing choices without special SQL seeds
or reset APIs. Validation contains WMS, AI and exactly the two canonical descriptors and uses
disposable test databases; successful conformance restores canonical flags to enabled.

## Functional HTTP gating

DISABLED means available and technically initialized, but functional entry is
blocked. Endpoints, routes, services and code remain registered; disabling does
not delete data or revert migrations. A request checks `ModuleStateService` once
at its module boundary, using the stable descriptor ID. It never reads the store
directly or captures an enabled flag during startup. Disable/re-enable takes effect
on the next request without restart, route rebuilding or reinitialization.
PostgreSQL remains the authority; no enabled-state cache is introduced.

The observable order is authentication, authorization, module gate, endpoint.
Existing protected endpoints return their normal 401 for invalid/missing credentials
and 403 for insufficient permissions before reaching the gate. An authorized caller
to a disabled module receives HTTP 503 with JSON exactly:

```json
{"code":"module_disabled"}
```

The response sets `Cache-Control: no-store`, with no `WWW-Authenticate`,
`Retry-After`, module ID, internal message or stack trace. A public module endpoint
also passes through the gate, without an authentication requirement of its own.

`ModuleDisabledException` belongs to Modularity and retains the module ID only
internally. `requireEnabled`/`RequireEnabledAsync` throws it only for an available
module with a persisted false flag. Unavailable IDs, missing state and unexpected
system failures retain their distinct internal errors; the HTTP gate does not
misrepresent them as administrative deactivation. The gate never changes state
or bypasses dependency validation.

CORE remains outside this gate: Health, authentication/login, user administration,
the runtime and future recovery/administration infrastructure are not functional
modules. All available modules still initialize technically even when disabled.
Future scheduled jobs, event consumers and other externally triggered internal
actions must use the same `ModuleStateService.requireEnabled` authority at their
entry boundary. The AI assistant uses the normal HTTP module gate and re-checks
child capability availability through CORE.
Future external modules receive the same HTTP semantics through the public adapters.

## Future lifecycle, not implemented

Installation will mean physical incorporation of a module package; uninstallation
will mean its physical removal. Both may initially require restart. DISABLE is
not UNINSTALL, and UNINSTALL does not imply deleting data. PURGE, if ever provided,
will be explicit and independent. None of these operations is implemented.

External directory discovery, JAR/DLL loading, hot loading/unloading, installers,
installation/uninstallation endpoints and frontend remain out of scope. No start/stop,
activation hooks, event bus or cache invalidation infrastructure is introduced.
The future package source will enter the existing registry/graph/runtime pipeline;
state remains keyed by descriptor ID independently of its source.

## .NET integration and current composition limit

`IAlxarafeModule.Descriptor` is the sole metadata source; `ConfigureServices` and
`InitializeAsync` retain their idiomatic .NET roles and cancellation support.
`ModuleDescriptor` is a sealed record with get-only `string Id`,
`int ModuleApiVersion` and `IReadOnlyList<string> Dependencies`. Construction
copies dependencies into a read-only collection. `ModuleApi.CurrentVersion` is
the supported version. `IModuleSource.GetModules()` supplies module instances.

`BuiltInAssemblyModuleSource` confines existing exported-type reflection and
construction to the explicitly selected built-in assemblies. It does not load
external assemblies. The Host explicitly selects WMS and AI entry assemblies,
referencing only their Module projects. It does not reference their
Domain/Application/Infrastructure/Http projects directly. Framework assemblies
remain independent of all business modules.
Future explicit sources enter the same registry/graph/runtime without a new loader.

`ModuleBuilder.AddSource` accepts additional sources. The existing
`AddModulesFromAssemblies`/`AddAlxarafeModules` convenience API wraps built-in
composition in a source. `Build()` gathers the registry, resolves the graph,
configures services in dependency order and registers the registry and runtime.
The Host uses `ModuleBuilder` directly to add production WMS/AI and the optional
conformance source before building once. All sources must participate before this build and before the Host's
`builder.Build()`. A future external assembly source can enter at that point;
no `AssemblyLoadContext` or external loader is implemented.

The platform EF adapter owns `PlatformDbContext`, `ModuleStateRow` and
`EfModuleStateStore`; the service and `IModuleStateStore` remain in framework-free
Modularity. Each check/transaction uses a fresh context from `IDbContextFactory`,
so tracked entities cannot become a permanent enabled-state cache.

Configure the explicit `ConnectionStrings__Platform` connection. Compose points it
at `alxarafe` (validation: `alxarafe_test`). Platform uses its own
`__PlatformMigrationsHistory`; migration `202610120001_ModuleStates` creates only
platform state and can coexist with existing module migration histories. Security
continues owning its separate context and tables. Testing rejects a Platform
connection outside the isolated validation database.

Before module initialization, the Host applies platform migrations and calls
`ModuleStateService.SynchronizeAsync`. The Host then performs one normal sequential
`InitializeAsync` call before mapping
module endpoints/running the application. The runtime owns a defensive read-only
copy of the ordered modules. `IHttpModule` remains a separate ASP.NET Core adapter;
Java need not reproduce this endpoint abstraction or service-collection API.

### Production WMS

WmsModule.Id is the single production ID source for erbas.wms. Its descriptor
uses ModuleApi.CurrentVersion and no dependencies. The five WMS projects own
domain invariants, application/port, EF infrastructure/migration, HTTP and entry
composition respectively. Domain/Application do not reference ASP.NET Core, EF,
Host or localization. Entry initialization applies its own EF migrations through
the existing runtime, including when administratively disabled.

WmsDbContext uses explicit ConnectionStrings:Wms, wms_uoms, wms_storage_attributes and its own
__WmsMigrationsHistory; no WMS entity belongs to Platform or Security contexts.
The unique C-collated code index supplies race-safe uniqueness and ordinal order.
Testing only permits alxarafe_test. Both catalogues use the same existing gate
and CORE administrator policy; disable/re-enable preserves both without restart.
Frozen UoM and Storage Attribute migrations remain unchanged. The third WMS
migration adds Item Families and the family/attribute join table under the same
WMS-owned context and history. All association endpoints inherit the existing
module gate; disabling WMS preserves catalogue and relation data. See
[WMS-003](verification/wms-003-dotnet.md).

### .NET HTTP adapter and OpenAPI

`MapAlxarafeModuleEndpoints` wraps each `IHttpModule` in a transparent empty
`RouteGroupBuilder`. Its `ModuleStateEndpointFilter` receives the real descriptor
ID; the module maps all routes against that protected builder. Nested groups inherit
the filter, including public endpoints. No path list, concrete module type or
manual per-handler check is needed. ASP.NET Core authentication/authorization
middleware runs before endpoint filters. The filter performs one enabled check
per request and converts only `ModuleDisabledException`; functional modules need
no manual checks.

The group also receives native `ProducesResponseTypeMetadata` for the JSON
`ModuleDisabledResponse` and HTTP 503. It is inherited by module endpoints and
appears in generated OpenAPI without per-endpoint duplication. Disabled endpoints
remain published; their route/operation metadata does not change with state.
CORE endpoints do not inherit this module response metadata. Functional gating
adds no shared contractual response; the Module API version remains unchanged.

## Verification

Focused modularity tests in the existing architecture test project cover
descriptor immutability, versions, sources, duplicate IDs, cross-source
dependencies, missing dependencies, direct/indirect cycles, deterministic order,
read-only collections, service configuration, sequential initialization and
failure propagation. PostgreSQL-backed `ModuleStateTests` cover defaults, retained
and reinstalled state, immediate checks, idempotent transitions, dependency chains,
technical initialization of disabled modules, rollback, case-sensitive IDs, clean
and existing-schema migrations, snapshot consistency and competing transitions
waiting on real database locks. `ModuleGatingTests` uses a test-only canonical Math
HTTP probe and PostgreSQL state to verify disable/re-enable without restart,
unchanged routes/migrations, CORE recovery access, 401/403 precedence,
exact 503 response, and persistent OpenAPI publication. A small TestServer context
also proves automatic gating of a test module's public nested groups, one state
read per entry, no gate writes, and propagation of missing/unavailable state.
Run complete Docker-only validation with the published contract:

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/pinned-erbas-contract ./bin/check
```

`ModulesApiTests` exercises the real HTTP/Identity/EF boundary, closed responses,
pagination including repeated keys and huge offsets, security precedence, retained
unavailable state, idempotence, both atomic conflicts, ordinal dependency ordering,
zero modules and internal failure classification. Composition tests prove the
normal registry contains WMS and AI, and canonical modules need both explicit switches.
The gating test includes CORE module listing while both canonical descriptors
are disabled. See [cleanup verification](verification/cleanup-001.md).
