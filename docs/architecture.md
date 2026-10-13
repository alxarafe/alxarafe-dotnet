# Architecture

ERBAS .NET is an API-only Core with independent modular/hexagonal boundaries.
The reusable projects are Core, Modularity, AspNetCore, EntityFrameworkCore,
Security, Security.AspNetCore, Security.EntityFrameworkCore and Host. Host is the
composition root; it contains no business domain or business assembly references.

## Module ownership and composition

Future modules own Domain, Application, Infrastructure, Http and Module boundaries.
Domain/Application depend on neither HTTP, ORM, security adapters nor localization.
Adapters implement application ports; endpoints remain thin. Modules must not
access sibling private tables/repositories. Core contains only transversal concepts.

One pipeline remains authoritative:

```text
ModuleSource(s) -> ModuleRegistry -> ModuleGraph -> ModuleRuntime
                              -> ModuleStateService / persistent state
```

`ModuleBuilder` accepts explicit sources before one Build. BuiltInAssemblyModuleSource
remains available for future controlled composition; no external plugin loader
exists. Normal startup has zero modules. Testing plus explicit conformance opt-in
registers only Math/Statistics descriptors; they are not business dependencies and
have no functional API. See [modularity](modularity.md).

Platform owns module_states and __PlatformMigrationsHistory through EF Core.
Security owns Identity separately. Development uses alxarafe/alxarafe_security;
validation uses alxarafe_test/alxarafe_security_test. Testing rejects non-test
connections. Startup uses official platform migrations and normal state synchronization.
Disabling modules does not remove data or revert migrations.

## HTTP, security and internationalization

Native authentication and authorization precede the persistent module endpoint
filter. Module state is checked per request; only administrative disable maps to
503 module_disabled. CORE Health/Auth/Users/Modules stay outside functional gating.
ModuleEndpointExtensions retains automatic mapping, nested-group filtering and
native 503 OpenAPI metadata. Runtime, state/store locks, dependency conflicts,
idempotence and atomicity are unchanged.

Permission definitions/providers and native policies remain generic. There are no
business permissions or seeded module accounts. DevelopmentUsersOptions remains
an extension point; tests verify it without shipping functional fixtures.

RequestLocalization selects en/es from Accept-Language, with English fallback.
CoreMessages belongs to the HTTP adapter, SecurityMessages to Security. Future
modules own their resources and narrow exception handlers; Host never owns their
texts. Native ProblemDetails and generic validation handling remain; shared
v0.5.0 closed errors are unchanged. See [i18n](i18n.md).

## Shared login and Identity

[AUTH-001](https://github.com/alxarafe/erbas-contract/blob/main/docs/auth-001.md) is the authority for login HTTP behavior. Register remains .NET-specific and unchanged. `LoginEndpoint` reads JSON with endpoint-local System.Text.Json options: case-sensitive names and disallowed unmapped members, followed by nonempty-string checks. Parser failures return the closed contractual error without parser details. Global JSON binding and module errors are unchanged.

Credential checks use `SignInManager.CheckPasswordSignInAsync` (including Identity prechecks and lockout), with email lookup owned by Identity. Accounts requiring a second factor cannot receive a password-only token. The principal comes from `CreateUserPrincipalAsync`, preserving configured claims and permissions. The public `BearerTokenOptions.BearerTokenProtector` for `IdentityConstants.BearerScheme` protects an `AuthenticationTicket` with expiry from the same scheme's clock and lifetime. The native bearer middleware reads this ticket; no new token system or internal framework API is used.

This separates credential validation and token protection from HTTP serialization because .NET 10's automatic sign-in handler writes expiry and refresh fields alongside the access token. Only `accessToken` is returned with `Cache-Control: no-store`; failures use the generic contractual JSON and Bearer challenge. No refresh token is issued. The internal token remains opaque and has no cross-backend interoperability requirement. See the [public protector API](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.authentication.bearertoken.bearertokenoptions.bearertokenprotector?view=aspnetcore-10.0).

## CORE users and current Identity state

USERS-001 and COLLECTIONS-001 use the existing Identity user/claim tables.
`erbas.core.enabled` and `erbas.core.admin` are implementation-specific persisted
state, distinct from `permission` claims. Absence means enabled=true/admin=false,
so existing Identity databases/users require no new state columns. New contractual
users receive explicit admin state; updates persist both flags. No additional
user table, new columns or migration framework is introduced. EnsureCreated
still initializes fresh Development/Testing databases; it is not used as an
upgrade mechanism. A small idempotent PostgreSQL startup upgrade changes only
the existing Email/NormalizedEmail types from varchar(256) to text, because the
shared email string has no maximum length. Fresh EF schemas use text directly;
native tests reproduce the previous schema and prove upgrade preserves accounts,
hashes and existing default state. Production startup also applies this type-only
upgrade to an already provisioned Identity database; it never seeds an admin.

The small `UserAdministration` EF adapter projects current state and handles
creation/updates. Shared creation uses UserManager's configured password hasher
and normalizer without running registration's stricter user/password validators.
The HTTP adapter validates the exact body and 12–256 Unicode code points first;
emails/passwords remain unchanged. Login resolves an exact email first and retains
Identity's normalized fallback when unambiguous. Registration preserves its own
validation policy and rejects normalized duplicates.

`CurrentUserMiddleware`, between authentication and authorization, resolves state
once for protected endpoints, removes stale CORE claims and supplies the current
principal/admin flag. Disabled/deleted users lose their authenticated principal,
including on module resources; re-enable/promotion/demotion affect the same token
on its next request. Public liveness and other anonymous endpoints do not query
user state. The shared authorization result handler emits closed 401/403 JSON
only on user endpoints and delegates unrelated errors to existing middleware.

Administrative endpoints use one native AuthorizationPolicy requiring current
CORE admin. Existing module policy providers and permission handlers remain
unchanged. Request bodies/queries are read inside authorized endpoint delegates
to preserve authorization precedence. Generated .NET OpenAPI documents these
endpoints and the .NET-only registration extension; shared OpenAPI remains the
sole common authority.

CORE creation, state updates and registration acquire `SHARE ROW EXCLUSIVE`
locks on AspNetUsers/AspNetUserClaims inside READ COMMITTED transactions before
reading/writing. Concurrent state updates therefore observe prior committed
changes before checking the last enabled admin. Normal identity reads stay
unblocked. Self-disable/demotion has no special exception. Pagination uses an
independent count and a parameterized id-ascending LIMIT/OFFSET query; public IDs
remain opaque. No cached totals, dynamic query infrastructure or generic locking
framework is introduced.

`POST /api/auth/register` is not part of ERBAS shared conformance. Portable clients
must not depend on it; it can change/disappear in a later .NET revision. It remains
in local OpenAPI/tests and defaults to enabled non-admin. Contractual creation
uses administrator-only `POST /api/users`. A dedicated configured seed admin has
no module permissions and never silently escalates an existing account.

## Verification and deliberate limits

Architecture tests assert framework/Host isolation from business assemblies and
resource ownership. Native integration uses real PostgreSQL and Identity; test-only
HTTP probes certify canonical gating, permission precedence, current-user state,
localized ProblemDetails and unchanged routes/migrations. Probes never enter Host.
Shared conformance uses only the published immutable v0.5.0 checkout. Local Bruno
covers Core Health and Identity registration/localization. ./bin/check remains the
single authority, with only its two temporary databases removed on success/failure.

No business module, WMS, AI, capability bus, dynamic loader or install/uninstall API
is implemented. Physical module loading and production Identity provisioning remain
separate future concerns. Historical experiment data is preserved in existing
installations without startup access or automated purge; new installations create
no retired business schema. See [cleanup evidence](verification/cleanup-001.md).
