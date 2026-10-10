# Usage

## Full validation

The reproducible validation flow for the platform foundation uses one command:

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/pinned-erbas-contract ./bin/check
```

The host uses Git, Bash, Docker and Docker Compose, plus the Linux fixture utilities listed in [API tests](../api-tests/README.md). It does not require .NET, Node.js, Bruno, or PostgreSQL. The flow validates Compose, the build, .NET tests, shared HTTP conformance, local OpenAPI, all platform and module Bruno scenarios (33 requests), authentication and authorization, ProblemDetails, localization, PostgreSQL persistence, and architecture tests. CI runs the same command. Validation uses temporary `alxarafe_test`, `alxarafe_security_test`, and `alxarafe_ai_test` databases in the existing PostgreSQL service and preserves development volumes.

`contract.revision` pins draft 0.4.0 at [42e0c5ad81902a355fe01d6635466717f46b9dfd](https://github.com/alxarafe/erbas-contract/tree/42e0c5ad81902a355fe01d6635466717f46b9dfd). Supply its separate clean checkout explicitly through `ERBAS_CONTRACT_DIR`; ignored local files are preserved. Shared OpenAPI and Bruno belong to erbas-contract and cannot be changed unilaterally by .NET. `bin/check` invokes that checkout's public `bin/test` against `http://validation-app:8080` on the existing Docker network, with no additional published port. Its 82 requests / 313 named checks cover Health, AUTH-001, USERS-001 and COLLECTIONS-001. A conformance failure fails the full validation, locally and in CI. A later README-only contract revision does not change this pin; use a separate clean detached checkout of the exact executable revision.

Validation creates a dedicated `admin@example.test` with a per-run random password
(host `od` and `tr`), supplied through `ERBAS_CORE_ADMIN_PASSWORD`. It has no module
permissions. Module reader/creator fixtures remain non-admin. Post-conformance
SQL verifies the administrator, disposable user state and credential hashes;
cleanup verifies removal of the validation Host and all three temporary databases.

The helper scripts are:

```bash
./bin/up       # validates Compose, builds, and waits for PostgreSQL/app healthy
./bin/test     # restores, builds, and tests in an isolated container and temporary databases
./bin/bruno    # starts the isolated Host, runs Bruno, and removes temporary databases
./bin/down     # stops services and preserves volumes
./bin/down -v  # stops services and removes development volumes
```

`./bin/test` creates three temporary databases (`alxarafe_test`, `alxarafe_security_test`, and `alxarafe_ai_test`) inside the Compose PostgreSQL container, runs the .NET tests against them, and drops them on exit. `./bin/check` retains them for the validation Host, Bruno, and SQL checks, then drops them even after failure. Development databases `alxarafe`, `alxarafe_security`, and `alxarafe_ai` are outside this flow.

All validation entry points wait for PostgreSQL to be healthy (up to 120 seconds) before running `psql`. Its healthcheck uses TCP so a fresh volume cannot report readiness from the temporary Unix-socket server used during initialization. CI runs the same flow against a fresh Compose environment.

Ports and network addresses:

Development binds only to loopback. Override `ERBAS_DOTNET_PORT` to change the
host port (for example, `ERBAS_DOTNET_PORT=49081 ./bin/up`); container port 8080
stays unchanged. PostgreSQL is not published. Validation uses Docker-network
URLs and does not require host port 48081. Dev Container forwarding of 8080 is
disabled because Compose owns the publication. See the
[shared port convention](https://github.com/alxarafe/erbas-contract/blob/main/docs/development-ports.md).

The public `/health` endpoint retains ASP.NET Core Health Checks and returns HTTP
200 application/json with exactly `{"status":"ok"}`. It probes HTTP process
liveness after startup, independently of PostgreSQL, modules or external services.

- From the host: `http://127.0.0.1:48081`.
- Inside the Docker network: `http://app:8080`.
- The health endpoint from the host is `http://127.0.0.1:48081/health`.
- Health inside Docker is `http://app:8080/health` (validation: `http://validation-app:8080/health`).
- OpenAPI from the host is `http://127.0.0.1:48081/openapi/v1.json`.

## Linux development with Docker only

The host needs Docker and Docker Compose; it does not need the .NET SDK, PostgreSQL, or `psql`.

```bash
git clone <repository-url> alxarafe-dotnet
cd alxarafe-dotnet
docker compose build
docker compose up -d
docker compose exec app dotnet restore
docker compose exec app dotnet build
./bin/test
curl http://127.0.0.1:48081/health
```

The PostgreSQL init script creates separate `alxarafe_security` and `alxarafe_ai` databases. On a fresh environment, the app creates the Identity schema and applies the Catalog EF Core migration during startup.

Development seed users exist only in Development or Testing:

- `reader@example.test` / `Reader_dev_only_123!` — `catalog.items.read`
- `creator@example.test` / `Creator_dev_only_123!` — `catalog.items.read`, `catalog.items.create`

Register and log in through the native API endpoints:

Registration is an implementation-specific Identity extension, outside the ERBAS
shared contract and shared Bruno coverage. Portable/common clients must not depend
on it; it may change or disappear in a future .NET revision. It retains Identity's
password policy and creates enabled non-admin users. The contractual administrative
creation route is `POST /api/users`.

```bash
curl -X POST http://127.0.0.1:48081/api/auth/register -H 'Content-Type: application/json' -d '{"email":"new@example.test","password":"Password123"}'
curl -X POST http://127.0.0.1:48081/api/auth/login -H 'Content-Type: application/json' -d '{"email":"creator@example.test","password":"Creator_dev_only_123!"}'
```

Use the returned bearer token for Catalog calls. Create and retrieve an item:

```bash
curl -X POST http://127.0.0.1:48081/api/catalog/items \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -d '{"sku":"CHAIR-001","name":"Office chair"}'

curl http://127.0.0.1:48081/api/catalog/items/<id> \
  -H 'Authorization: Bearer <token>'
```

OpenAPI is available at `http://127.0.0.1:48081/openapi/v1.json`. Run all platform and module Bruno requests reproducibly through Docker:

```bash
./bin/bruno
```

The Docker runner uses the `docker` environment and reaches `validation-app`, which connects only to the temporary databases. The independent platform, Catalog, and AiAgent suites cover authentication, 401/403, permissions, CRUD access, ProblemDetails, localization, and health.

View logs with `docker compose logs -f app`. Stop with `docker compose down`; remove the development databases too with `docker compose down -v`.

## Shared CORE user administration

Fresh Identity schemas use text for Email/NormalizedEmail. Startup applies a small
idempotent type upgrade to previously provisioned varchar(256) columns, preserving
existing users and credentials. This avoids imposing an email maximum absent
from the shared contract; EnsureCreated is not treated as an upgrade mechanism.

Configure a dedicated development administrator explicitly with
`ERBAS_CORE_ADMIN_PASSWORD` before `./bin/up`; no administrator password is supplied
by default. Set `ERBAS_CORE_ADMIN_EMAIL` to override the standalone development fallback
`admin@example.test`. Compose maps these variables to `SecuritySeed__AdminEmail`
and `SecuritySeed__AdminPassword`. The client-owned full-stack demo maps its
effective shared administrator to them; .NET does not read the contract defaults
file or depend on its filesystem location. Provisioning runs only in
Development/Testing, is idempotent and never overwrites existing credentials/state.
An existing disabled/non-admin account at that email causes a seed conflict rather
than implicit escalation.

`GET /api/auth/me` returns exactly opaque string `id`, `email`, `enabled`, `admin`.
`GET /api/users`, `GET /api/users/{id}`, `POST /api/users` and
`PATCH /api/users/{id}` require current CORE admin; module permissions do not imply
admin and admin does not grant module permissions. Every protected request reads
current persisted CORE state before authorization, so old tokens stop working when
disabled and reflect promotion/demotion without a new login. Login while disabled
returns neutral `401 invalid_credentials`.

Creation accepts exactly nonempty `email`, `password` and Boolean `admin`; new
users are enabled. Passwords contain 12–256 Unicode code points without trimming
or composition rules and use the existing Identity hasher. Email is stored as
received, with exact-duplicate conflict. Exact email login takes precedence;
Identity's normalized fallback remains available when unambiguous.
PATCH accepts only non-null Boolean `enabled` and/or `admin`, at least one.
Concurrent state changes cannot disable/demote the last enabled admin. Self-changes
are allowed if another enabled admin remains.

The list uses optional integer `offset` (default 0, minimum 0) and `limit`
(default 50, range 1–100). It returns exactly `items`, `offset`, `limit`, `total`,
`order`, with fixed `[{"field":"id","direction":"asc"}]`. Ordering precedes the
window; total counts all users before pagination. At/beyond-total offsets return
200 with empty items and preserved metadata. Invalid values are rejected without
clamping. No filters, search, selectable ordering or cursor pagination exist.

All shared user responses are JSON/no-store. Closed errors contain only `code`:
400 `invalid_request`, protected 401 `unauthorized` (Bearer challenge), 403
`forbidden` (no challenge), 404 `user_not_found`, 409 `email_conflict` or
`last_admin`. Authentication/authorization precedes lookup and body/query parsing.
There is no DELETE, email update, password management or new RBAC.
See [verification](verification/users-001-collections-001.md).

## Architecture

`Catalog.Domain` has no framework, persistence, security, or localization dependencies. `Catalog.Application` owns use cases and ports. Infrastructure implements persistence with an internal EF model; HTTP is a transport adapter. `Catalog.Module` composes those pieces and declares permissions. The Host explicitly discovers `CatalogModule` and owns the global composition root. See [docs/architecture.md](architecture.md), [the first decision record](decisions/0001-modular-hexagonal-foundation.md), and [the platform decision](decisions/0002-platform-security-localization.md).

## Adding a module

Create Domain, Application, Infrastructure, Http, and Module projects under `src/Modules/<Name>/`. Implement `IAlxarafeModule`, expose only public contracts, and add the module assembly explicitly in Host. A module must never reach into another module’s Infrastructure; use public contracts or future public events.

## AiAgent 0.1

AiAgent currently provides a preparatory knowledge base shared by the installation, with permission-protected creation/readback. No AI provider or search is connected. See [the module decisions, Unicode limits, migration requirements, and future roadmap](ai-agent.md).

Run its independent HTTP suite with `./bin/bruno modules/ai-agent`. `./bin/check` includes all three suites and all module tests.
