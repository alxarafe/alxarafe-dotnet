# Alxarafe.NET

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![CI](https://img.shields.io/github/actions/workflow/status/alxarafe/alxarafe-dotnet/ci.yml?branch=main&style=flat-square&label=CI)](https://github.com/alxarafe/alxarafe-dotnet/actions/workflows/ci.yml)
[![Docker](https://img.shields.io/badge/Docker-supported-2496ED?style=flat-square)](https://www.docker.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-4169E1?style=flat-square)](https://www.postgresql.org/)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen?style=flat-square)](https://github.com/alxarafe/alxarafe-dotnet/pulls)

> Alxarafe.NET is an independent modular application framework for .NET, designed natively for the .NET ecosystem.

This experimental foundation uses .NET 10, ASP.NET Core Minimal APIs, EF Core, PostgreSQL, native dependency injection, explicit modules, and hexagonal boundaries. Platform features use native ASP.NET Core Identity bearer authentication, policies, localization, ProblemDetails, and OpenAPI.

## Full validation

The reproducible validation flow for the platform foundation uses one command:

```bash
./bin/check
```

The host only needs Docker and Docker Compose. It does not require .NET, Node.js, Bruno, or PostgreSQL. The flow validates Compose, the build, .NET tests, health, OpenAPI, all platform and module Bruno scenarios (32 requests), authentication and authorization, ProblemDetails, localization, PostgreSQL persistence, and architecture tests. CI runs the same command. Validation uses temporary `alxarafe_test`, `alxarafe_security_test`, and `alxarafe_ai_test` databases in the existing PostgreSQL service and preserves development volumes.

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

The current `/health` response uses ASP.NET Core's health-check representation.
Shared JSON conformance (`{"status":"ok"}`) is known debt for CONTRACT-001C;
PLATFORM-001 preserves this behavior and checks HTTP 200 and existing validation.

- From the host: `http://127.0.0.1:48081`.
- Inside the Docker network: `http://app:8080`.
- The health endpoint from the host is `http://127.0.0.1:48081/health`.
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

## Architecture

`Catalog.Domain` has no framework, persistence, security, or localization dependencies. `Catalog.Application` owns use cases and ports. Infrastructure implements persistence with an internal EF model; HTTP is a transport adapter. `Catalog.Module` composes those pieces and declares permissions. The Host explicitly discovers `CatalogModule` and owns the global composition root. See [docs/architecture.md](docs/architecture.md), [the first decision record](docs/decisions/0001-modular-hexagonal-foundation.md), and [the platform decision](docs/decisions/0002-platform-security-localization.md).

## Adding a module

Create Domain, Application, Infrastructure, Http, and Module projects under `src/Modules/<Name>/`. Implement `IAlxarafeModule`, expose only public contracts, and add the module assembly explicitly in Host. A module must never reach into another module’s Infrastructure; use public contracts or future public events.

## AiAgent 0.1

AiAgent currently provides a preparatory knowledge base shared by the installation, with permission-protected creation/readback. No AI provider or search is connected. See [the module decisions, Unicode limits, migration requirements, and future roadmap](docs/ai-agent.md).

Run its independent HTTP suite with `./bin/bruno modules/ai-agent`. `./bin/check` includes all three suites and all module tests.
