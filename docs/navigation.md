# Effective application navigation (NAVIGATION-CONTRACT-001)

The backend formally adopts published immutable erbas-contract v0.13.0, including
the separate Generic Resource Runtime CRUD and effective application navigation
protocols. Local and CI checks verify the published tag's immutable identity and
run its shared conformance directly. No candidate opt-in or frontend change is
required. The shared navigation specification is `docs/core/navigation-001.md`
in the supplied published contract checkout.

## Composition and boundaries

`Alxarafe.Navigation` is a small independent entry assembly, not a property of
ResourceDefinition. Modules register INavigationDefinitionProvider through their
existing service contribution hook. NavigationRegistry snapshots declarations and
validates them eagerly before persistence initialization/HTTP. No Assets logic
exists in framework navigation, and no navigation is added to resource schemas.

Validation covers registered owner, globally unique owner-qualified stable ID,
label/permission/icon shape, valid parent, same-owner hierarchy, cycles, group
versus actionable parent, supported target, local route safety, and existing
bound list-capable resource targets. Unknown authorization policies fail closed
during effective resolution, using the existing authority. Invalid declarations
fail startup. Definitions copy permission collections and expose no mutation API.

RESOURCE refers only to a resource ID; ResourceRegistry remains authoritative for
its schema and list security. NavigationService checks both owning and target
modules, target binding scope, list permission and optional stricter contribution
permissions. ROUTE is an application-local path with explicit existing permission
requirements and optional tenant scope. It creates no page, report or dashboard
framework and does not validate frontend route installation.

Group/parent declarations stay inside one contributing module for this first
version. Every module may contribute independent roots. Actionable nodes are
leaves; empty groups are pruned recursively. Siblings use order ascending then
ordinal ID ascending, independent of provider, dictionary or module registration.

## Effective context, authorization and errors

GET /api/meta/navigation requires authoritative current authentication. Query
parameters are rejected with the common 400 envelope. Tenant context is resolved
fresh, without a new tenant-selection/header mechanism. Ordinary missing/inactive/
ambiguous context hides tenant-scoped entries; platform entries may remain and an
empty menu is 200. Context configuration failure returns the existing 503 code.
Disabled modules are omitted; unavailable state resolution fails safely using
the common error envelope. Unexpected failures return resource_execution_failed
without exception detail. No navigation-specific error shape is introduced.

Existing IResourceAuthorization evaluates effective permissions. Definitions never
send permission names, claims or visibility rules to clients. Navigation is only
presentation/discovery: after permission revocation even an old visible menu target
is still denied by Resource Runtime. No CRUD/action guards or their precedence
were changed. Metadata discovery can contain resources not offered in navigation.

## Localization, representation and caching

The response is a tree: `{version, items}`; nodes carry id, final label, order,
children and optional icon/target. Groups omit target; leaves have empty children.
RESOURCE targets carry type/resource; ROUTE targets type/route. Icons are opaque
identifiers, not image URLs or a frontend-library contract.

Modules use native IStringLocalizer resources, optionally declaring the label's
resource marker internally. Literal labels are also supported. Assets owns English
and Spanish .resx resources; standard Accept-Language negotiation, parent-culture
and English fallback remain unchanged. No translation keys/types enter the payload.

NavigationService has no global response cache. It recomputes effective entries
before If-None-Match evaluation. The opaque version fingerprints current identity,
resolved context/denial state, language and final ordered representation. The ETag
is the quoted version. Private, no-cache and Vary Authorization/Accept-Language
are sent on 200/304. Equal trees for different users/tenants do not share a version.
Changes not affecting effective navigation need not invalidate it. There is no
atomic snapshot promise while permissions/modules change during projection;
target endpoints remain authoritative and later revalidation recomputes state.

Clients fetch after authentication/context bootstrap, keep navigation in memory,
revalidate with ETag when needed and clear body plus ETag on logout/context change.
There is no per-route polling or server-push infrastructure. Future invalidation
can refer to the opaque version; GET remains canonical.

## Proving contribution and limits

Assets contributes Assets → Asset register, Asset types, Locations. The type-admin
entry additionally requires existing schema administration permission. Internal
field/value storage and custody history resources are not enumerated automatically.
This is curated presentation, not a second schema registry or an ITAM feature.

Tests also contribute synthetic local routes, icons, tied ordering and platform
entries to prove the generic mechanism without adding production placeholder pages.
No client code, route registry, workflow, dynamic-field composition, bulk UI,
record-version protocol, SignalR or SSE is introduced. The authoritative published
check is documented in [generic resource contract validation](generic-resource-contract.md#validation).

## Historical preparation verification

The following evidence predates formal published v0.13.0 adoption. Its references
to the old pin and candidate opt-in describe those historical runs; that opt-in
has since been removed from `bin/check`.

- Focused Docker integration/architecture validation exercised navigation and
  existing Assets behavior; full validation is the final authority.
- `ERBAS_CONTRACT_DIR=/path/to/clean/v0.12.0 ./bin/check` passed with 1,093
  native tests, zero failures/skips, and published v0.12.0 live conformance.
- The same full command with explicit `ERBAS_CONTRACT_CANDIDATE_DIR` pointing
  to the extended unpublished v0.13.0 candidate passed independently on fresh
  disposable infrastructure, including the new shared navigation requests.
- Both runs passed persistence, sensitive-log and database/container cleanup
  checks. Contract repository full verification passed 218 tests; its focused
  navigation scope passed 5 tests, including deliberate invalid-response faults.
- Two existing isolated-module test compositions were adapted to remove navigation
  contributions when replacing installed modules. Strict production validation
  was preserved. No Resource Runtime, business lifecycle, persistence, published
  dependency pin or frontend behavior changed.
- Final diff/scope, credentials/NUL and documentation-link review passed.
