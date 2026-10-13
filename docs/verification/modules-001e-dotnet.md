# MODULES-001E-DOTNET: published contract v0.5.0 adoption

## Result

On 2026-10-13, the complete authoritative Docker flow passed with exit 0:

```bash
ERBAS_CONTRACT_DIR=/tmp/erbas-modules-001e-contract-v0.5.0 ./bin/check
```

`contract.revision` and CI now select published `v0.5.0`. The release tag
and supplied clean checkout HEAD both resolved to the required immutable commit:

```text
35be07fe002c364f758ae50419d0ecb86d1f0c43
```

The original supplied erbas-contract repository was only read. Validation used
a separate clean release checkout, preserving the original branch and files.
Existing preflight rejects unexpected root/origin, dirty checkouts, wrong HEAD,
missing tags and unexpected targets before provisioning. No contract/main or
unpublished fallback is used.

## HTTP, state and composition

Host Minimal APIs expose CORE module listing and enabled-state changes, outside
the functional module endpoint group. The existing current-user middleware and
CORE admin policy apply before parsing/lookup/mutation, with exact 401/403 JSON
and a Bearer challenge only on 401. All specified responses are JSON/no-store.
The closed representation uses descriptor metadata, ordinal sorted dependencies
and fresh persisted enabled state. The registry is the sole AVAILABLE authority;
retained rows for absent modules remain hidden.

The extracted COLLECTIONS-001 query parser, bounds, fixed order metadata and
offset converter serve both Users and Modules without changing user wire
behavior. PATCH rejects malformed/non-object JSON, missing/null/nonboolean enabled,
incorrect casing, extra fields and duplicates. Successful idempotent transitions
return the complete Module.

Narrow unavailable/dependency-conflict exceptions identify only 404/409.
Missing state and unexpected storage errors remain internal failures.
The existing EF store, transaction and table lock are unchanged; snapshot
validation happens before writes, with no cascades or compensating rollback.

| Composition | AVAILABLE module IDs |
| --- | --- |
| Normal | AiAgent, Catalog |
| Explicit validation | AiAgent, Catalog, erbas.dev.math, erbas.dev.statistics |

One ModuleBuilder combines the existing built-in assembly source and the optional
ConformanceModuleSource before a single Build. The source requires both Testing
and Modules:ConformanceEnabled=true (Modules__ConformanceEnabled in Compose).
Testing alone does not activate it; Development/Production ignore the opt-in.
Math has no dependencies; Statistics depends exactly on Math. Both use the current
Module API version and normal SynchronizeAsync initialization, initially enabled
in a clean validation database. They have no functional API or mathematical work.

The published shared suite explicitly permits additional AVAILABLE modules and
discovers its canonical descriptors through pagination. No separate deterministic
replacement of Catalog/AiAgent was needed. Normal business composition, services,
permissions, migrations, endpoint gating and functionality remain intact.

## Native and shared verification

| Suite | Before | After | Failures | Skips |
| --- | ---: | ---: | ---: | ---: |
| Architecture tests | 33 | 33 | 0 | 0 |
| Catalog domain | 2 | 2 | 0 | 0 |
| Catalog integration | 9 | 9 | 0 | 0 |
| Host integration | 83 | 132 | 0 | 0 |
| AiAgent domain | 26 | 26 | 0 | 0 |
| AiAgent integration | 49 | 49 | 0 | 0 |
| Total | 202 | 251 | 0 | 0 |

The total separates into 61 architecture/domain tests and 190 integration tests
(previously 141 integration tests). All 49 added cases passed: 44 module HTTP
cases, four composition cases and one PostgreSQL missing-state regression.
Existing state tests now require precise exception types; the existing functional
gating test also verifies CORE module listing while business modules are disabled.
The architecture test retains its Host-only composition boundary with the new
ModuleBuilder expression.

The baseline was measured before implementation with bin/test and the same
generated disposable CORE administrator used by bin/check. An initial standalone
probe without that administrator failed three existing admin-dependent cases;
repeating with the authority's normal seed configuration passed all 202 tests.
No AUTH/USERS fix, token or Identity redesign was made.

New native coverage includes closed/default/zero-module lists, ordinal ID and
dependency ordering before paging, boundaries and huge offsets, repeated/invalid
queries, stale retained rows, strict bodies, both idempotent transitions, real
Identity security precedence and response headers, persistent state across requests,
both dependency conflicts with unchanged snapshots, CORE recovery and unexpected
state/storage errors. Composition tests check required descriptors/dependency,
normal business preservation and normal state initialization.

The full published shared collection passed: **130 requests / 506 named checks**.
Its inventory was checked at the pinned tag, including the four inherited checks
for each of 48 module requests. The four-module validation set fits one discovery
page. Detailed shared diagnostics remain withheld by its existing runner.
The unchanged local platform/Catalog/AiAgent suites also passed: 32 requests,
3 named tests and 67 assertions in total. Existing controlled-process/watchdog
and cleanup helper tests remain present and passing.

Two complete bin/check executions passed; the final one followed the import and
named-limit review. Both solution builds completed with zero warnings/errors.
Docker dotnet format --no-restore --verify-no-changes passed for every changed
C# file after fixing one import-order finding. Shell syntax and git diff --check
passed. Local documentation links were also checked.

## Cleanup, scope and documentation

Final output included:

```text
POST_CONFORMANCE_DATABASE=passed CORE_ADMIN=separate-from-module-permissions
LOG_SECRECY=passed
VALIDATION PASS: Compose, .NET, API, Bruno, PostgreSQL, and architecture checks completed
VALIDATION_DATABASE_CLEANUP=passed
```

Cleanup removed the validation Host and all three disposable databases.
The existing PostgreSQL service, Docker network and development volumes remain
available by design; development databases are outside the validation flow.
Native test containers use --rm. No shared test targeted persistent development.

Current references were updated in README, documentation index, usage, modularity
and the working agreement. Earlier v0.4.0 verification remains historical.
New meaningful values use existing ASP.NET/platform constants or narrow named
constants for routes/errors, pagination bounds/order and canonical IDs/activation.
Incidental fixture values remain local. No meaningful magic literals were added.

Files in this task:

```text
.github/workflows/ci.yml
AGENTS.md
README.md
bin/check
contract.revision
docker-compose.yml
docs/README.md
docs/modularity.md
docs/usage.md
docs/verification/modules-001e-dotnet.md
src/Alxarafe.Host/CollectionPagination.cs
src/Alxarafe.Host/ConformanceModuleSource.cs
src/Alxarafe.Host/CurrentUserMiddleware.cs
src/Alxarafe.Host/ModuleEndpoints.cs
src/Alxarafe.Host/Program.cs
src/Alxarafe.Host/UserEndpoints.cs
src/Alxarafe.Modularity/ModuleDependencyConflictException.cs
src/Alxarafe.Modularity/ModuleStateService.cs
src/Alxarafe.Modularity/ModuleUnavailableException.cs
tests/Alxarafe.ArchitectureTests/HostBoundaryTests.cs
tests/Alxarafe.Host.IntegrationTests/ModuleCompositionTests.cs
tests/Alxarafe.Host.IntegrationTests/ModuleGatingTests.cs
tests/Alxarafe.Host.IntegrationTests/ModuleStateTests.cs
tests/Alxarafe.Host.IntegrationTests/ModulesApiTests.cs
```

No erbas-contract changes, copied shared OpenAPI/Bruno, new dependencies,
Math/Statistics functionality, business behavior changes, capability registry,
external/hot loader, Java or Angular changes are included. The existing local
.NET-specific API suites and generated OpenAPI remain implementation tools.
No push, PR, merge, tag creation, release or deployment was performed.
