# Alxarafe.NET

> Alxarafe.NET is not a port of Alxarafe PHP. It is an independent modular application framework for .NET, sharing architectural principles while being designed natively for the .NET ecosystem.

This experimental foundation uses .NET 10, ASP.NET Core Minimal APIs, EF Core, PostgreSQL, native dependency injection, explicit modules, and hexagonal boundaries. It has no Orchard dependency and does not aim to clone another framework. Platform features use native ASP.NET Core Identity bearer authentication, policies, localization, ProblemDetails, and OpenAPI.

## Validación completa

La comprobación reproducible de la iteración 0.2 se ejecuta con un único comando:

```bash
./bin/check
```

Sólo requiere Docker y Docker Compose en el host. No requiere instalar .NET, Node.js, Bruno ni PostgreSQL. El flujo valida Compose, build, tests .NET, health, OpenAPI, los 14 escenarios Bruno, autenticación/autorización, ProblemDetails, localización, persistencia PostgreSQL y tests arquitectónicos. En CI se ejecuta el mismo comando con `CI=true` y limpia los contenedores y volúmenes al terminar.

Los scripts auxiliares son:

```bash
./bin/up       # valida Compose, construye y espera a PostgreSQL/app healthy
./bin/test     # restore, build y test dentro de app
./bin/bruno    # ejecuta api-tests/bruno dentro del servicio bruno
./bin/down     # detiene servicios y conserva volúmenes
./bin/down -v  # detiene servicios y elimina volúmenes de desarrollo
```

Puertos y nombres de red:

- Desde el host: `http://localhost:8081`.
- Dentro de la red Docker: `http://app:8080`.
- El endpoint de salud desde el host es `http://localhost:8081/health`.
- OpenAPI desde el host es `http://localhost:8081/openapi/v1.json`.

## Linux development with Docker only

The host needs Docker and Docker Compose; it does not need the .NET SDK, PostgreSQL, or `psql`.

```bash
git clone <repository-url> alxarafe-dotnet
cd alxarafe-dotnet
docker compose build
docker compose up -d
docker compose exec app dotnet restore
docker compose exec app dotnet build
docker compose exec app dotnet test
curl http://localhost:8081/health
```

The PostgreSQL init script creates a separate `alxarafe_security` database. On a fresh environment, the app creates the Identity schema and applies the Catalog EF Core migration during startup. If the database volume predates this iteration, run `docker compose down -v` before starting again.

Development seed users exist only in Development or Testing:

- `reader@example.test` / `Reader_dev_only_123!` — `catalog.items.read`
- `creator@example.test` / `Creator_dev_only_123!` — `catalog.items.read`, `catalog.items.create`

Register and log in through the native API endpoints:

```bash
curl -X POST http://localhost:8081/api/auth/register -H 'Content-Type: application/json' -d '{"email":"new@example.test","password":"Password123"}'
curl -X POST http://localhost:8081/api/auth/login -H 'Content-Type: application/json' -d '{"email":"creator@example.test","password":"Creator_dev_only_123!"}'
```

Use the returned bearer token for Catalog calls. Create and retrieve an item:

```bash
curl -X POST http://localhost:8081/api/catalog/items \
  -H 'Content-Type: application/json' \
  -d '{"sku":"CHAIR-001","name":"Office chair"}'

curl http://localhost:8081/api/catalog/items/<id>
```

OpenAPI is available at `http://localhost:8081/openapi/v1.json`. Run all 14 Bruno requests reproducibly through Docker after the app is up:

```bash
docker compose run --rm bruno
```

The Docker runner uses the `docker` environment and reaches the app by its Compose service name. The `local` environment is for a CLI running outside Compose and uses `http://localhost:8081`. The collection covers authentication, 401/403, permissions, CRUD access, ProblemDetails, localization, and health.

View logs with `docker compose logs -f app`. Stop with `docker compose down`; remove the development databases too with `docker compose down -v`.

## Architecture

`Catalog.Domain` has no framework, persistence, security, or localization dependencies. `Catalog.Application` owns use cases and ports. Infrastructure implements persistence with an internal EF model; HTTP is a transport adapter. `Catalog.Module` composes those pieces and declares permissions. The Host explicitly discovers `CatalogModule` and owns the global composition root. See [docs/architecture.md](docs/architecture.md), [the first decision record](docs/decisions/0001-modular-hexagonal-foundation.md), and [the platform decision](docs/decisions/0002-platform-security-localization.md).

## Adding a module

Create Domain, Application, Infrastructure, Http, and Module projects under `src/Modules/<Name>/`. Implement `IAlxarafeModule`, expose only public contracts, and add the module assembly explicitly in Host. A module must never reach into another module’s Infrastructure; use public contracts or future public events.
