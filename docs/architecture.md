# Architecture

ERBAS .NET is an API-only Core with independent modular/hexagonal boundaries.
The reusable projects are Core, Modularity, AspNetCore, EntityFrameworkCore,
Security, Security.AspNetCore, Security.EntityFrameworkCore and Host. Host is the
composition root; it contains no business domain and references only the WMS and
AI entry assemblies for explicit built-in composition. Framework projects never
reference business modules.

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
selects the explicit WMS and AI entry assemblies; no external plugin loader exists.
Normal startup registers erbas.wms and erbas.ai. Testing plus explicit conformance
opt-in additionally registers Math/Statistics descriptors; they are not business
dependencies and have no functional API. See [modularity](modularity.md).

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
v0.8.0 errors use the closed code/errors envelope without human messages. See [i18n](i18n.md).

## Shared login and Identity

[AUTH-001](https://github.com/alxarafe/erbas-contract/blob/v0.8.0/docs/core/auth-001.md) is the authority for login HTTP behavior. Register remains .NET-specific and unchanged. `LoginEndpoint` reads JSON with endpoint-local System.Text.Json options: case-sensitive names and disallowed unmapped members, followed by nonempty-string checks. Parser failures return the closed contractual error without parser details. Global JSON binding and module errors are unchanged.

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
user state. The shared authorization result handler emits closed 401/403 JSON on endpoints
marked with WithSharedContractErrors metadata and delegates unrelated errors to
existing middleware. Users, CORE modules, WMS and AI opt in; there is no per-module path list.

Administrative endpoints reuse CoreAdministration.Policy in Security.AspNetCore,
requiring current CORE admin. Existing module policy providers and permission handlers remain
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

## AI assistant

The AI module owns Application, Infrastructure, Http and Module projects. Its
Application orchestrator combines effective capabilities and independently
registered module READ primitives through neutral shared ports. Capabilities retain
CORE-CAP-002 execution; primitives use a controlled router with authoritative
persisted identity resolution at discovery and before each execution. AI never
references WMS/persistence or accepts arbitrary HTTP. Successful typed identities
are returned in ordered `toolsUsed`; input is untrusted and budgets remain finite.
See [AI-API-001 design](ai-read-primitives.md), including the published v0.12.0
response extension and deterministic conformance validation.

Infrastructure resolves `AI:Provider` behind `IAiModelClient`. Groq and local
Ollama reuse the common OpenAI-compatible chat client. Groq retains its existing
error/retry policy; Ollama supplies only configuration and explicit no-auth
transport, with no provider-specific retry. Model selection uses `AI:Model` independently
of the provider name, retaining the legacy Groq model alias. Testing selects the
deterministic adapter. Application depends only on its model port; provider
selection cannot change effective tools or authorization. See
[canonical provider configuration and capabilities](ai-providers.md).
The common abstraction does not imply functional equivalence: every observable
difference, limitation, configuration requirement or unvalidated capability must
be documented by provider and, when relevant, by model. Adding a provider requires
implementation, tests, configuration, a capability matrix, known limitations and
smoke evidence when viable. No provider may expand the caller's effective tools
or bypass authorization.

The Http adapter owns closed request validation, the public
response/error envelope, `no-store` behavior and AI audit linkage. The Module entry
registers the AI permission policy, model selection, endpoint mapping and audit
middleware. The Host still exposes only the entry assemblies; framework projects
do not reference AI projects.

AI audit records the AI invocation ID, subject, timestamp, outcome, duration,
correlation ID, safe failure code and successful capability/CORE invocation IDs.
It does not log messages, prompts, provider bodies, credentials or capability
payloads. CORE audit remains authoritative for each capability invocation.
Primitive audit separately links AI correlation, sequence, duration, logical
outcome and bounded paging counts, without arguments/results.

## Verification and deliberate limits

Architecture tests assert framework isolation from business assemblies, Host's
entry-only WMS/AI references, pure WMS domain/application boundaries and
resource ownership. Native integration uses real PostgreSQL and Identity; test-only
HTTP probes certify canonical gating, permission precedence, current-user state,
localized ProblemDetails and unchanged routes/migrations. Probes never enter Host.
Shared conformance uses the pinned v0.12.0 checkout. Local Bruno
covers Core Health and Identity registration/localization. ./bin/check remains the
single authority, with only its two temporary databases removed on success/failure.

WMS owns UoM, Storage Attribute and Item Family creation/listing plus atomic
family/attribute N:M associations, with domain/application/EF/HTTP/entry projects.
Its separate context, connection and migration history preserve Platform and
Security ownership. Shared collection parsing/order/offset serialization now live
in AspNetCore, without a second pagination implementation. CORE exposes authenticated
capability discovery and synchronous READ invocation. CORE resolves the effective
descriptor, enforces module availability and required permissions before body
validation, validates closed input and provider output, and audits the trusted
server-side invocation context. In-process module executors own the existing
business behavior; no internal HTTP loopback or capability bus is introduced.
WMS also owns the [inventory foundation](wms-inventory.md): warehouse/location
masters, stockable references, immutable movements and balances derived from the
ledger. These public .NET-specific extensions reuse existing UoMs/families and
module boundaries and add no AI capabilities or shared contract behavior.
Storage rules, advanced topology, a dynamic loader and install/uninstall APIs
remain outside scope. Physical module loading and production Identity provisioning remain
separate future concerns. Historical experiment data is preserved in existing
installations without startup access or automated purge; new installations create
no retired business schema. See [cleanup evidence](verification/cleanup-001.md).
