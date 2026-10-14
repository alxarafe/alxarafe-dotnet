# WMS-001N: production UoM and contract v0.6.0

## Scope and immutable contract

.NET adopts published `erbas-contract v0.6.0` at
`92d1282154f340aadcfdf93f52a82a1f3472b335`. The supplied checkout was clean and
HEAD matched the tag. `contract.revision`, `bin/check`'s immutable identity
validation and CI's explicit tag checkout now agree. Root/origin/cleanliness
checks and matching HEAD remain required; Java and the contract were not modified.

The only new business operations are POST and GET `/api/wms/uoms`.
The sole shared collection remains in erbas-contract; no local WMS Bruno copy
or implementation-specific contract exception was introduced.

## Module and architecture

`WmsModule.Id` is the single production identity source for `erbas.wms`.
The descriptor uses `ModuleApi.CurrentVersion` and has no dependencies.
Host selects only `typeof(WmsModule).Assembly` through the existing
`BuiltInAssemblyModuleSource` and `ModuleBuilder`. Registry, graph, runtime,
state synchronization and endpoint mapping remain the normal shared pipeline.

The five projects under `src/Modules/Wms` own Domain, Application, Infrastructure,
Http and Module entry composition. Domain/Application are independent of HTTP,
EF, Host and localization. The framework assemblies have no business references.
Architecture tests check both compiled references and project references;
Host can reference only the WMS entry assembly/type for composition, without
direct WMS Domain/Application/Infrastructure/Http references.

Normal composition contains WMS. Math/Statistics remain technical conformance
descriptors requiring Testing plus explicit opt-in; neither is a WMS dependency.

## Persistence and observable behavior

WMS owns `WmsDbContext`, `ConnectionStrings:Wms`, `wms_uoms` and
`__WmsMigrationsHistory`. Migration `202610140001_Uoms` creates only id, code
and description plus identity/unique-code indexes. Platform/Security contexts
and existing migrations are unchanged. Testing rejects missing WMS connections
and databases other than `alxarafe_test`. Development can share the physical
functional database while retaining separate context and migration ownership.

The application generates non-nil UUIDs without prescribing a contractual version.
PostgreSQL enforces uniqueness; parameterized INSERT with ON CONFLICT (code)
handles concurrent duplicates without a preflight SELECT, overwrite or disclosure
of database diagnostics. Duplicate code maps only to 409
`wms_uom_code_conflict`. Unexpected storage failures remain internal failures.

Codes match exactly `^[A-Z][A-Z0-9]{0,9}$` with no normalization.
Description trims only edge ASCII space/TAB/CR/LF, then validates 1–255 Unicode
scalar values with native Rune decoding. NUL and unpaired surrogates are invalid.
Internal content, case and whitespace are preserved; POST and GET return the
normalized persisted user text, without translation.

System.Text.Json parsing inside the delegate rejects non-object roots, malformed
or trailing JSON, duplicates, unknown/missing fields, wrong casing/types and nulls.
POST accepts no query parameters. CollectionPagination, CollectionOrder and
OffsetJsonConverter were moved minimally from Host to AspNetCore without changing
their existing behavior. WMS adds its closed query boundary and code ASC order.
EF performs parameterized PostgreSQL LIMIT/OFFSET; count and window share a
repeatable-read snapshot. Huge and beyond-total offsets return empty windows.

## Security, gating and i18n

CoreAdministration.Policy in Security.AspNetCore reuses the current administrator
claim. SharedContractErrors metadata opts endpoints into exact closed 401/403
responses through the transversal native authorization result handler. CORE
endpoints and WMS share this mechanism without adding WMS paths to CORE lists
or introducing permissions/RBAC.

Authentication and current Identity state precede authorization, then the existing
ModuleStateEndpointFilter, then functional parsing/operation. WMS never calls
RequireEnabledAsync manually in its handlers. JSON/no-store, the 401-only Bearer
challenge and closed 400/409/503 errors are preserved. CORE disable/re-enable
affects the next request without restart and retains catalogue data.
No human error fields, global envelope redesign or empty localization bundles
were introduced.

## Validation (2026-10-14)

Executed twice successfully, including a final run after formatting and scope review:

```bash
ERBAS_CONTRACT_DIR=/home/rsanjose/Desarrollo/Alxarafe/erbas-contract ./bin/check
git diff --check
bash -n bin/check bin/test
```

| Validation | Result |
| --- | --- |
| Build | Successful; zero warnings/errors |
| Architecture and pure tests | 53 passed; zero failures/skips |
| Real PostgreSQL/HTTP integration | 203 passed; zero failures/skips |
| New coverage | 27 pure WMS cases, 46 WMS integration cases and one additional architectural check |
| Shared published conformance | Exit 0; 182 requests / 714 named checks, retaining all prior 130 / 506 |
| Local platform Bruno | 3/3 requests, 1/1 named test, 5/5 assertions |
| Fresh WMS installation | Empty catalogue; separate Platform/WMS migration histories and expected columns verified |
| EF/PostgreSQL | Applied/pending migrations, snapshot consistency, constraints, normalized Unicode persistence, concurrent uniqueness, ordinal order and pagination passed |
| Security/runtime | Real bearer/admin precedence, disabled-user token rejection, exact headers/errors and dynamic disable/re-enable with retained data passed |
| Format | Docker dotnet format whitespace completed with exit 0 for this task's C# paths |
| Secrets | LOG_SECRECY=passed |
| Cleanup | Validation Host removed; both ephemeral databases dropped; VALIDATION_DATABASE_CLEANUP=passed |
| Final source/shell checks | git diff --check and bash -n passed |

Shared diagnostics remain withheld for credential secrecy. Collection totals
were independently counted from the unchanged published files. Existing
development databases/volumes and the repository's PostgreSQL/cache infrastructure
remain preserved by the existing cleanup policy. No development database was used
for native or shared mutating tests.

Historical verification reports retain their original revisions and results.
No Item, ItemFamily, Warehouse, Location, Inventory, Handling Unit, stock, AI,
new RBAC, event bus, plugin loader or frontend was implemented. No push, PR,
merge, tag, release or deployment was performed.
