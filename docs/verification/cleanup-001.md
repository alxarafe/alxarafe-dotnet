# CLEANUP-001: remove experimental business modules

## Audit and implemented scope

Started from clean, updated main at 5510c12 (I18N-001 merged). Deleted the empty
feature/contact-002 local branch and created
feature/cleanup-001-remove-experimental-modules. No outside ERP design was reused.

Catalog and AiAgent were complete .NET-only experimental modules, not published
ERBAS v0.5.0 capabilities. Their Domain/Application/Infrastructure/Http/Module
assemblies, CRUD models, endpoints, permissions, resources, seeds, migrations and
local collections were module-specific. They have been removed, not renamed into
WMS or future AI. Ten production projects and four module test projects were
removed from Alxarafe.sln, with all Host/architecture project references removed.

Removed roots: src/Modules/Catalog, src/Modules/AiAgent,
tests/Alxarafe.Modules.Catalog.*, tests/Alxarafe.Modules.AiAgent.*,
api-tests/modules/catalog and api-tests/modules/ai-agent. Module-only architecture
checks were retired. Historical design moved to docs/history/experimental-ai-agent.md;
previous verification reports are explicitly historical and retain original results.
No retired module remains in active code, projects, DI, resources, tests, Bruno,
generated OpenAPI, Docker configuration or seed definitions.

## Transversal infrastructure preserved

Core/Modularity/AspNetCore/EntityFrameworkCore/Security adapters and Host remain.
ModuleDescriptor, sources (including BuiltInAssemblyModuleSource), one registry,
graph/runtime, sequential initialization, persistent enable/disable, dependency
conflicts, table locking/atomicity and automatic HTTP filtering remain unchanged.
Generic permission providers/policies/claims and DevelopmentUsersOptions survive;
no module account/permission is now seeded by normal composition.

Normal composition has zero modules. Testing plus explicit
Modules:ConformanceEnabled registers only erbas.dev.math and erbas.dev.statistics,
with the existing dependency and initial enabled policy. They have no functional
API or business behavior. Core administration remains outside gating.

I18N-001's native negotiation, en/es and English fallback, Core/Security resources,
IdentityErrorDescriber, native ProblemDetails and transversal validation handling
remain. Only retired owner resources/markers were deleted. AGENTS.md permanently
documents module ownership, Core neutrality and no functional dev dependencies.

## Persistence decision

Removed only the retired migration assemblies and their snapshots:

- 202610050001_InitialCatalog;
- 202610060001_InitialKnowledge;
- 20261008000100_KnowledgeTextLimits.

This is removal of whole experimental owners, not rewriting a surviving migration
or replaying different SQL under an applied ID. Their original code remains in
Git history. Platform's 202610120001_ModuleStates and separate history are unchanged;
Security persistence is unchanged. A new installation no longer creates retired
tables or the old additional business database. A fresh validation test verifies
Platform contains only module_states and __PlatformMigrationsHistory.

Existing development databases/tables and applied histories were not purged or
modified. Removal does not imply uninstall/purge of retained data. No destructive
upgrade or rollback script was introduced; any future purge requires separate
explicit authorization. Validation now creates/drops only alxarafe_test and
alxarafe_security_test; its Platform variable no longer carries a business name.

## Coverage transfer

Baseline: 281 tests (34 architecture, 28 module domain, 219 integration).
Retired 98 module domain/integration cases and nine module-only architecture cases.
All 149 prior Host cases remain, with affected assertions transferred to Core:

- RuntimeProbe lives exclusively in the test assembly and maps a minimal Math
  route through the real permission/Identity pipeline and persisted gate.
- Dynamic disable/re-enable, 401/403 before 503, current disabled-user state,
  exact module_disabled headers/body, Core recovery, route/OpenAPI publication,
  unchanged migration history and runtime identity survive without restart.
- Existing public nested-group automatic mapping/counting-store tests remain,
  including one read per entry, no gate writes and distinct internal failures.
- Native Core 401/500 localization, Security parametrized validation, regional,
  weighted, missing/unsupported headers and request isolation remain.
- Added six native transversal 400 locale cases, one fresh-schema/Core-only
  OpenAPI case and one generic idempotent permission-seed case (eight total).
- Architecture/resource checks now assert Host/framework have no business assembly,
  project, type or source dependencies; Security owns all native Identity messages.

No production probe, extra dev module, mathematical endpoint or new dependency
was introduced. Shared conformance was neither duplicated nor shortened.

## Authoritative verification (2026-10-13)

```bash
ERBAS_CONTRACT_DIR=/tmp/erbas-modules-001e-contract-v0.5.0 ./bin/check
```

Final run exited 0 and printed VALIDATION PASS and
VALIDATION_DATABASE_CLEANUP=passed. Build: zero warnings/errors. Tests:

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Architecture/runtime | 25 | 0 | 0 |
| Core HTTP/PostgreSQL integration | 157 | 0 | 0 |
| Total | 182 | 0 | 0 |

Full shared Bruno passed 130 requests / 506 named checks; local Core Bruno passed
three requests, one named test and five assertions. The former 31 local business
requests no longer describe installed capabilities. Native tests retain their
transversal guarantees. OpenAPI, real migrations, state/credential invariants,
log secrecy and SQL checks passed. Intermediate fixture compile errors were
corrected before final validation; no production workaround or watchdog change
was used. Changed C# passed Docker dotnet format --verify-no-changes.

contract.revision remains v0.5.0, with tag/HEAD verified at
35be07fe002c364f758ae50419d0ecb86d1f0c43. Both supplied and original contract
repositories remained clean and unchanged.

Artifact inspection inside the built image reported PUBLISHED_CORE=present,
RETIRED_MODULES=absent and TEST_ASSEMBLIES=absent. Core/HTTP/Security assemblies
were present, and no retired or integration-test assembly shipped in /app/publish.

## Cleanup, documentation and scope review

The validation Host and both test databases were removed. Manual PostgreSQL checks
found zero test databases, including the previously used third test name. Existing
development database names/OIDs matched before/after final verification, including
the retained old experiment database. Only the existing healthy PostgreSQL service
remained; development volumes were preserved.

Broad retired-name searches leave only this removal evidence, explicit historical
reports/design/index links and ordinary words such as translation catalog. No
active module vestige remains. Reviewed new meaningful literals use native enums,
existing PlatformStorage/CoreMessages constants and narrow test-owned route/
permission constants; no global constants or translation framework was invented.
git diff --check passed. Current README/usage/architecture/modularity/i18n and ADR
examples were updated; historical evidence was not rewritten as current.

No erbas-contract change, revision bump, WMS or new AI implementation, new package,
push, PR, merge, tag, release, publication or deployment occurred. WMS-001 requires
its own approved domain study; old experiments and canonical dev descriptors must
not become its functional dependencies.
