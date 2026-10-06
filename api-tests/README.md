# Bruno API tests

The independent collections complement .NET domain, architecture, and integration tests. They target the isolated validation Host at `http://validation-app:8080` inside Compose.

```text
api-tests/
    platform/                 # health, registration, login (3 requests)
    modules/
        catalog/              # Catalog permissions and functionality (13 requests)
            assert-persistence.sh
    shared/
        environments/docker.bru
```

Each collection has its own `bruno.json`. The `environments` directories are relative symlinks to the shared environment, so the CLI and Bruno UI use the same base URL and test-only identity credentials without duplicated files. Shared infrastructure contains no module entity, error, or request state.

Platform generates a unique registration email and verifies its own login. Catalog prepares reader and creator sessions explicitly in its collection, uses `readerToken` and `creatorToken`, and generates its own unique SKU. Create/read/duplicate scenarios use `itemId`/`sku` from Catalog's own create flow. No variable or token is inherited from another collection, so Catalog can run before or without Platform. Its local create/read ordering is intentional. The original 14 scenarios are preserved; two duplicate-item cases add localized 409 coverage. Missing-item cases also assert the Spanish/English titles.

Run all 16 requests without installing Bruno, Node.js, or the .NET SDK on the host:

```bash
./bin/bruno
```

Run a single collection independently:

```bash
./bin/bruno platform
./bin/bruno modules/catalog
```

`bin/bruno` provisions `alxarafe_test` and `alxarafe_security_test`, starts the validation Host, runs the selected suites in separate CLI invocations, and removes the databases on exit. Development databases/volumes are preserved. The internal `--prepared` mode reuses the lifecycle managed by `bin/check`.

`./bin/check` is the authoritative full validation entry point. It runs .NET tests, every module collection with a `bruno.json`, platform checks, and each module's `assert-persistence.sh`. Catalog owns its SQL and endpoint OpenAPI assertions; the global runner only orchestrates module scripts. Add new suites under `modules/<name>` with a `bruno.json` and a link to the shared environment. Keep any module-specific test setup and assertions inside that suite.
