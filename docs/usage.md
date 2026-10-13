# Usage

## Full validation

Docker and Docker Compose are required; host .NET, Bruno, Node and PostgreSQL are not.

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/clean-v0.5.0-checkout ./bin/check
```

The checkout must have the expected origin, no tracked/nonignored changes, HEAD and
v0.5.0 tag at 35be07fe002c364f758ae50419d0ecb86d1f0c43. bin/check never modifies it.
CI uses the same authority and pinned release. Check executes restore/build/native
architecture and integration tests, clean migrations, isolated Host startup, full
shared Bruno, local Core Bruno (three requests), OpenAPI/persistence/secrecy checks
and cleanup. Any failure propagates nonzero.

Validation uses only alxarafe_test and alxarafe_security_test. Existing development
databases and volumes are preserved. The validation Host is removed and both test
databases are dropped even after failure. bin/test runs native tests through Docker;
bin/bruno runs isolated HTTP checks. These are components of bin/check, not substitutes.
No shared test is run against persistent development.

Testing plus Modules:ConformanceEnabled=true registers the two canonical dev
modules. Validation Compose enables it explicitly. Normal startup has zero modules;
Development alone never opts in. Startup synchronizes their initially enabled state
through the normal pipeline. Shared conformance changes/restores both states.
They expose no business or mathematical endpoints.

## Development

```bash
./bin/up
./bin/down
ERBAS_DOTNET_PORT=49081 ./bin/up
```

Development defaults to 127.0.0.1:48081 (container 8080); PostgreSQL is unpublished.
The volume persists across stops. Do not use bin/down --volumes to clean validation:
it deletes development storage. A fresh volume creates alxarafe and alxarafe_security;
Identity uses its existing initialization and Platform applies official EF migrations.
ConnectionStrings__Platform and ConnectionStrings__Security select the respective
contexts. Existing obsolete experiment databases/tables are neither accessed nor
purged by startup. Retiring their projects does not rewrite applied migration history.

No module users or business permissions are seeded. A local CORE administrator
requires explicit ERBAS_CORE_ADMIN_PASSWORD; ERBAS_CORE_ADMIN_EMAIL defaults to
admin@example.test. Seed does not reset credentials or escalate an existing account.
Validation always supplies its own generated administrator password.

## API and diagnostics

Health is public process liveness, independent of database/modules. Auth login,
current identity, user and module administration implement published v0.5.0.
POST /api/auth/register remains .NET-specific; portable clients must not depend on it.
CORE admin does not imply functional module permissions. Authentication/authorization
precede parsing/lookup and functional gating. Local OpenAPI is available at
/openapi/v1.json and remains one schema, without business operations or translated names.

Use Accept-Language: en/es; absent/unsupported headers fall back to English and
regional variants use native parent matching. Technical codes and fields remain stable.
See [internationalization](i18n.md) and [modularity](modularity.md).

```bash
docker compose ps
docker compose logs app postgres
./bin/bruno platform
```

Do not disclose bearer tokens, passwords or sensitive request data. Validation
withholds application logs in diagnostics and asserts known credentials are absent.
Native tests serialize test projects sharing the same ephemeral databases to avoid
schema/seed races. Test-only probes are never part of the production Host artifact.

## Current limits

No business module, WMS, new AI, external plugin loader, hot loading, module
installation/uninstallation, capability bus or production provisioning workflow exists.
Future modules own their domain, resources, permissions, endpoints, persistence and
tests and cannot depend on canonical dev descriptors. See [architecture](architecture.md)
and [cleanup verification](verification/cleanup-001.md).
