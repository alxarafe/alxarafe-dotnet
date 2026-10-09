# Bruno API tests

The independent collections complement .NET domain, architecture, and integration tests. They target the isolated validation Host at `http://validation-app:8080` inside Compose.

```text
api-tests/
    platform/                 # health, registration (2 requests)
    modules/
        catalog/              # Catalog permissions and functionality (13 requests)
            assert-persistence.sh
        ai-agent/             # independent knowledge API (17 requests)
            assert-persistence.sh
    shared/
        environments/docker.bru
```

Each collection has its own `bruno.json`. The `environments` directories are relative symlinks to the shared environment, so the CLI and Bruno UI use the same base URL and test-only identity credentials without duplicated files. Shared infrastructure contains no module entity, error, or request state.

Platform generates a unique registration email. Shared login conformance belongs exclusively to the pinned erbas-contract AUTH-001 collection; the duplicated platform login request has been removed. Module login requests remain session setup for permission and functionality tests. Catalog prepares reader and creator sessions explicitly in its collection, uses `readerToken` and `creatorToken`, and generates its own unique SKU. Create/read/duplicate scenarios use `itemId`/`sku` from Catalog's own create flow. No variable or token is inherited from another collection, so Catalog can run before or without Platform. Its local create/read ordering is intentional. The original 14 scenarios are preserved; two duplicate-item cases add localized 409 coverage. Missing-item cases also assert the Spanish/English titles.

Run all 32 requests without installing Bruno, Node.js, or the .NET SDK on the host:

```bash
./bin/bruno
```

Run a single collection independently:

```bash
./bin/bruno platform
./bin/bruno modules/catalog
./bin/bruno modules/ai-agent
```

`bin/bruno` provisions `alxarafe_test`, `alxarafe_security_test`, and `alxarafe_ai_test`, starts the validation Host, runs the selected suites in separate CLI invocations, and removes the databases on exit. Development databases/volumes are preserved. The internal `--prepared` mode reuses the lifecycle managed by `bin/check`.

`./bin/check` is the authoritative full validation entry point. It supplies AUTH-001 with the existing reader fixture email from the shared environment and password from the running Testing Host configuration (`SecuritySeed__ReaderPassword`). `SecuritySeed` provisions that identity through `DevelopmentUsersOptions` before readiness, only in Testing/Development. No production account is introduced. It runs .NET tests, every module collection with a `bruno.json`, platform checks, and each module's `assert-persistence.sh`. Catalog owns its SQL and endpoint OpenAPI assertions; the global runner only orchestrates module scripts. Add new suites under `modules/<name>` with a `bruno.json` and a link to the shared environment. Keep any module-specific test setup and assertions inside that suite.

AiAgent prepares its own `ai-reader`/`ai-writer` sessions, `aiReaderToken`/`aiWriterToken`, and knowledge data. It reuses only shared test credentials and the base URL. Its assertions cover unauthenticated access, read/write permissions, installation-wide creation/readback, invalid input and oversized text, Spanish/English 404 responses, the exact three-column schema, migration history, and named length constraints. See [the module decisions](../docs/ai-agent.md) for the Unicode length semantics.

AiAgent's `prepare.sh` bounds the complete preparation with a Bash watchdog and an exclusive session/process group created by `setsid`, including the Docker environment check and PostgreSQL execution. Defaults are 90 seconds overall (`AI_AGENT_PREPARE_TIMEOUT_SECONDS`), a 5-second TERM-to-KILL grace (`AI_AGENT_PREPARE_KILL_AFTER_SECONDS`), a 15-second connection limit (`AI_AGENT_DB_CONNECT_TIMEOUT_SECONDS`), a 30,000 ms statement limit (`AI_AGENT_DB_STATEMENT_TIMEOUT_MILLISECONDS`), and a 10,000 ms lock limit (`AI_AGENT_DB_LOCK_TIMEOUT_MILLISECONDS`). Overrides must be positive integers up to 2,147,483,647; obsolete task-prefixed names are rejected. These variables configure test preparation only, not the application runtime. Supervisor deadlines return 124; ordinary Docker/psql exit codes, including 124 and 137, are preserved.

Runtimes remain in Docker; the host launcher requires Linux with `/proc`, Bash, Docker with Compose, `setsid` from util-linux, and the `mktemp`, `mkfifo` and `rm` utilities. Preparation validates the session leader's PID, parent, PGID, SID and start time through `/proc` before allowing database operations or signalling its group. It sends TERM at the deadline and KILL after the grace period, keeping the leader present until the watchdog is collected. INT, TERM and unexpected exits clean only the verified group and private temporary files; subsequent INT/TERM signals are ignored during cleanup, preserving the first exit status. This supervisor belongs only to AiAgent preparation and does not depend on a `timeout` utility. `test-prepare.sh` uses controlled Docker/setsid doubles and verifies resistant workers, children, grandchildren, repeated signals, exit codes, watchdog cancellation and cleanup; it runs inside the existing app image before the live preparation.
