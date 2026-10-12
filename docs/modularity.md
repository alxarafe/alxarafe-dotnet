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
initialization remains technical, and functional activation is a future layer.

The CORE is not a disableable functional module. Basic Health, essential
authentication, the modular runtime and essential system administration remain
outside functional module activation. AUTH/USERS behavior is unchanged.

Modules must not access another module's internal tables or private repositories.
Future integration must use public contracts/capabilities of the owning module,
including future Inventory and AI integrations. No bus or capability registry is
introduced without a real consumer.

## Approved future lifecycle, not implemented

Enabled/disabled will be persistent and dynamic, without restart. Disabling must
never delete data. Routes and services may remain registered while runtime checks
block functionality. Jobs and event consumers must respect the same state;
dependencies must prevent invalid activation combinations. No activation state,
storage, HTTP administration or activation hooks exist yet.

Installation will mean physical incorporation of a module package; uninstallation
will mean its physical removal. Both may initially require restart. DISABLE is
not UNINSTALL, and UNINSTALL does not imply deleting data. PURGE, if ever provided,
will be explicit and independent. None of these operations is implemented.

External directory discovery, JAR/DLL loading, hot loading/unloading, installers,
state persistence, module management endpoints and frontend are out of scope.
The future package source will enter the existing registry/graph/runtime pipeline;
its loader and package distribution details require later implementation.

## .NET integration and current composition limit

`IAlxarafeModule.Descriptor` is the sole metadata source; `ConfigureServices` and
`InitializeAsync` retain their idiomatic .NET roles and cancellation support.
`ModuleDescriptor` is a sealed record with get-only `string Id`,
`int ModuleApiVersion` and `IReadOnlyList<string> Dependencies`. Construction
copies dependencies into a read-only collection. `ModuleApi.CurrentVersion` is
the supported version. `IModuleSource.GetModules()` supplies module instances.

`BuiltInAssemblyModuleSource` confines existing exported-type reflection and
construction to the explicitly selected built-in assemblies. It does not load
external assemblies. `Program.cs` still selects the Catalog and AiAgent entry
assemblies as temporary composition-root knowledge. No concrete module knowledge
reaches the registry, graph or runtime. Existing IDs `Catalog` and `AiAgent`,
services, endpoint mapping, permissions and persistence remain unchanged.

`ModuleBuilder.AddSource` accepts additional sources. The existing
`AddModulesFromAssemblies`/`AddAlxarafeModules` convenience API wraps built-in
composition in a source. `Build()` gathers the registry, resolves the graph,
configures services in dependency order and registers the registry and runtime.
All sources must participate before this build and before the Host's
`builder.Build()`. A future external assembly source can enter at that point;
no `AssemblyLoadContext` or external loader is implemented.

The Host performs one normal sequential `InitializeAsync` call before mapping
module endpoints/running the application. The runtime owns a defensive read-only
copy of the ordered modules. `IHttpModule` remains a separate ASP.NET Core adapter;
Java need not reproduce this endpoint abstraction or service-collection API.

## Verification

Focused modularity tests in the existing architecture test project cover
descriptor immutability, versions, sources, duplicate IDs, cross-source
dependencies, missing dependencies, direct/indirect cycles, deterministic order,
read-only collections, service configuration, sequential initialization and
failure propagation. Existing Catalog/AiAgent composition and functional suites
remain in place. Run complete Docker-only validation with the published contract:

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/pinned-erbas-contract ./bin/check
```
