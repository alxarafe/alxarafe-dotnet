# USERS-001 and COLLECTIONS-001 — .NET verification

The .NET backend implements the shared draft **0.4.0** at immutable executable
revision `42e0c5ad81902a355fe01d6635466717f46b9dfd`. The same SHA is pinned in
`contract.revision` and CI checkout. The workflow display name is now
`.NET CI / shared conformance`; its jobs, triggers and validation command are
unchanged. Later README-only contract commits do not change the executable pin.

## Identity state and authorization

The existing ASP.NET Core Identity users and claims tables remain authoritative.
There is no additional user table or column. CORE enabled/admin state
uses `erbas.core.enabled` / `erbas.core.admin`, separately from module `permission`
claims. Absence means enabled=true/admin=false, preserving pre-existing accounts
and fresh Identity initialization. Existing-schema compatibility is exercised by
module users and native Identity registration without these claims. EnsureCreated
is still a fresh-schema initializer; it is not an upgrade mechanism.

The existing Email/NormalizedEmail varchar(256) limit was incompatible with the
contract's unbounded nonempty email string. Fresh EF schemas now use text; a small
idempotent startup SQL upgrade changes those existing columns to text, in seed
environments and already provisioned production databases. A native test recreates
the old types, runs the upgrade twice and verifies the reader's identity, email,
password hash and default state remain unchanged. A separate HTTP test creates
and authenticates a user with an email exceeding 300 characters. No general
migration framework or unrelated schema change is introduced.

CurrentUserMiddleware runs between authentication and authorization on protected
resources. It resolves the current persisted user once, removes stale CORE flags
from the bearer principal and applies current admin. Disabled/deleted users lose
authenticated access to all protected resources, including Catalog/AiAgent.
Re-enable, promotion and demotion affect existing tokens on their next request.
Public Health skips this lookup; a native test blocks database queries after
startup and still obtains exact 200 Health with a valid bearer and zero queries.
Identity bearer protection, lockout and second-factor checks remain in place.

One native AuthorizationPolicy requires current CORE admin for all user
administration routes. This is independent of existing module permission policies:
admin grants no module permission and reader/creator permissions grant no admin.
The authorization result handler delegates unrelated errors to existing native
handling; shared endpoints receive only their closed JSON errors.

The dedicated `admin@example.test` fixture has no module permissions. Its seed
runs only in Development/Testing with an explicit configured password, absent by
default in development. Validation generates a new password each run and forwards
it by environment. Concurrent/repeated seed is idempotent, preserves credentials,
and rejects existing disabled/non-admin accounts without escalation or overwrite.

## Shared HTTP behavior

| Endpoint | Authorization | Success |
| --- | --- | --- |
| GET /api/auth/me | Authenticated enabled user | 200, current User |
| GET /api/users | Current CORE admin | 200, paged user envelope |
| GET /api/users/{id} | Current CORE admin | 200, User |
| POST /api/users | Current CORE admin | 201, enabled User |
| PATCH /api/users/{id} | Current CORE admin | 200, updated User |

User contains exactly opaque string id, email, enabled and admin. Current identity
is reused from request state. Unparseable/unknown authorized IDs produce 404.
Authentication/authorization precedes lookup, body parsing and query validation.
Bodies are parsed as strict JSON objects inside endpoint delegates, with exact
case-sensitive allowed fields, no duplicates, nulls, scalar coercion or extras.
Creation requires email/password/admin; PATCH requires enabled and/or admin.

Creation passwords have inclusive **12–256 Unicode code points**, counted with
EnumerateRunes, without trimming/normalization/composition constraints. Creation
uses UserManager's configured native Identity password hasher, independently of
registration's stricter validators. Email is stored unchanged and exact duplicate
creation conflicts. Login resolves exact email first, retaining Identity's
normalization fallback for unambiguous lookup. Native coverage proves distinct
case-variant identities can log in without ambiguous normalized-email exceptions.

All shared user responses are application/json and Cache-Control no-store:

| Status | Exact code | Meaning |
| --- | --- | --- |
| 400 | invalid_request | Invalid JSON/body/pagination |
| 401 | unauthorized | Missing/invalid/expired/disabled protected bearer; Bearer challenge |
| 403 | forbidden | Authenticated non-admin; no challenge |
| 404 | user_not_found | Unknown/unusable ID after authorization |
| 409 | email_conflict | Exactly duplicate email |
| 409 | last_admin | Would remove the last enabled administrator |

Bodies contain only code. Disabled login retains neutral AUTH-001
401 invalid_credentials. No Identity validation details, password/hash, token
internals or ProblemDetails escape from shared user requests. Unrelated module
ProblemDetails/localization behavior is preserved.

## Atomic state updates and pagination

CORE creation/state changes and registration acquire SHARE ROW EXCLUSIVE locks on
AspNetUsers/AspNetUserClaims inside READ COMMITTED transactions before reading or
writing. The last-admin check therefore observes serialized committed state.
Disabled administrators do not count. Self-disable/demotion succeeds when another
enabled admin remains; the current request completes, subsequent access reflects
the change. There is no distributed lock, generic policy engine or token deletion.

Native concurrency tests isolate CORE claims in the disposable database, restoring
them afterwards. A third PostgreSQL transaction holds the lock until pg_stat_activity
proves both worker transactions are waiting. Releasing it yields exactly one
Updated and one LastAdmin result, with one enabled admin remaining. All four
disable/demote combinations pass. Readiness polling does not determine correctness.
Host tests are serialized because these cases temporarily isolate administrator
state; worker transactions still use independent concurrent scopes/connections.

The collection contains exactly items, offset, limit, total and order. Defaults
are offset 0 / limit 50, with offset nonnegative and limit 1–100. BigInteger parsing
and a response-only converter preserve offsets beyond native integer ranges;
at/beyond-total offsets return 200 with empty items and unchanged metadata.
Total is an independent COUNT of all users before pagination, not item count.
Parameterized PostgreSQL id ASC LIMIT/OFFSET runs before slicing, with declared
order `[{"field":"id","direction":"asc"}]`. IDs/comparison stay backend-owned.
No filters, ordering input, cursors, derived fields or timing metadata are added.

## .NET-specific registration and OpenAPI

POST /api/auth/register remains implemented, locally tested and present in generated
.NET OpenAPI. It is outside the ERBAS shared contract and shared Bruno conformance.
Portable/common clients must not depend on it; it may change/disappear in a future
.NET revision. It retains Identity's password/user validation and normalized
duplicate policy, creates enabled non-admin accounts, and never substitutes for
contractual administrator creation via POST /api/users.

Generated .NET OpenAPI documents the new routes, bearer authorization, user/page
representations, pagination defaults/bounds and optional non-null PATCH fields.
Native metadata tests and bin/check retain register/login presence checks and add
the shared user routes. Generated OpenAPI does not replace the shared authority.

## Validation (2026-10-10)

Executed using Docker-only versioned infrastructure:

```bash
ERBAS_CONTRACT_DIR=/tmp/erbas-dotnet-contract-040 ./bin/check
git diff --check
```

The complete bin/check returned exit 0, including post-conformance SQL, log
secrecy and verified cleanup. Docker-based dotnet format was also run successfully
with --no-restore and an explicit include list limited to this task's C# files.
The final diff check passed.

The temporary clean detached contract clone is exactly the pinned SHA; the sibling
contract checkout and its branch are untouched. Native results have zero failures
and skips:

| Test project | Passed |
| --- | ---: |
| Architecture | 13 |
| Catalog Domain | 2 |
| Catalog Integration | 9 |
| Host Integration | 74 |
| AiAgent Domain | 26 |
| AiAgent Integration | 49 |
| **Total** | **173** |

This comprises 46 pure native/unit/architecture checks and 127 PostgreSQL-backed
cases (five Host transformer cases do not use PostgreSQL). The three integration
projects together contain 132 cases. Existing tests remain intact; 63 Host cases
were added for users, current bearer state, Unicode boundaries, register policy,
seed behavior, last-admin concurrency, pagination and generated OpenAPI.

The real shared suite passes **82 requests / 313 named checks**, covering Health,
AUTH-001, USERS-001 and COLLECTIONS-001 without skips or copied shared requests.
The local platform/Catalog/AiAgent Bruno suites retain their 33 requests and pass,
including the existing module preparation/process-supervision checks. Register,
Catalog/AiAgent permissions, localization and native error behavior regressions
pass. No new shared scenarios are duplicated in api-tests.

Fresh validation databases initialize successfully. Post-conformance SQL verifies
the enabled CORE admin without module permissions, existence and final enabled/
non-admin state of disposable shared users, and nonempty hashes distinct from the
validation plaintext. Existing module migration/OpenAPI/persistence assertions
remain active. Logs pass credential/token/hash secrecy checks. Failure diagnostics
withhold application logs. Cleanup removes the validation Host and verifies all
three temporary databases are absent, preserving development databases/volumes.

Scope/documentation/sensitive-data reviews cover only .NET shared users/collections,
necessary validation infrastructure and current documentation. Historical evidence
is preserved. Related repository README statuses were reviewed read-only; coordinated
status updates are a separate post-merge task. Java, Angular and the shared contract
are unchanged. No push, PR, merge, release or deployment is authorized.

The two contracts are implemented in one atomic change because .NET introduces
the list directly in its required paged form; no legacy array endpoint is introduced
as a failing intermediate revision. There are no remaining conformance blockers.
