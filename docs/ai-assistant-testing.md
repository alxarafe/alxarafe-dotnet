# Local AI assistant testing

This guide verifies the AI assistant end to end after provider configuration
for local Development. It is intended for developers, evaluators and
technical users running the backend locally with Docker.

Use synthetic, non-sensitive data. Real business data disclosure to an external
provider is outside AI-001 and must be addressed before production use.

## Architecture and secret boundary

```text
erbas-client
     |
     | POST /api/ai/assistant/invocations
     v
alxarafe-dotnet
     |
     | backend-only provider connection
     v
  selected provider
  (Groq or local Ollama)
```

| Repository | Responsibility |
| --- | --- |
| `erbas-contract` | Defines the provider-neutral public HTTP contract. It never stores provider credentials. |
| `alxarafe-dotnet` | Runs the backend and owns the local AI provider configuration. |
| `erbas-client` | Talks only to ERBAS. It must never receive the Groq API key. |

The browser must never call the provider directly. Provider credentials, when
needed, belong only to the backend runtime. Local Ollama needs no API key.

Provider/model selection and all provider-specific settings belong to the
[canonical AI provider reference](ai-providers.md). It also defines the capability
states, known limitations, retry policies and how to switch providers safely.

## Provider, model and protocol

Choose provider and model separately as described in
[provider selection](ai-providers.md#provider-and-model-are-separate).
The common transport does not make Groq and Ollama functionally equivalent; use
the [capability matrix](ai-providers.md#capability-matrix) for the evidence scope.
This guide covers the subsequent authenticated testing workflow, not a second
configuration reference.

## Loopback-only Ollama and the local smoke test

See [Docker reachability and deployment limits](ai-providers.md#docker-reachability-and-deployment-boundary)
and the [detailed Ollama smoke evidence](verification/ai-provider-001b.md).
The successful host-network experiment is not the normal deployment design;
`./bin/up` alone does not solve the host-loopback listener mismatch.

## Configure the backend

Follow [configure or switch provider/model](ai-providers.md#configure-or-switch-providermodel),
using the existing `./bin/configure-ai` helper. It saves local settings and makes
no provider call. Keep provider secrets in the ignored backend configuration.

The local administrator also needs a password:

```dotenv
ERBAS_CORE_ADMIN_EMAIL=admin@example.test
ERBAS_CORE_ADMIN_PASSWORD=
```

Set `ERBAS_CORE_ADMIN_PASSWORD` to a new local-only password. Do not reuse a real
password. `ERBAS_CORE_ADMIN_EMAIL` defaults to `admin@example.test`.

## Start the backend

```bash
./bin/up
```

The default local endpoint is `http://127.0.0.1:48081`. Startup waits for
PostgreSQL and the application to become healthy.

## Authenticate

The examples use `jq` to read JSON; substitute another JSON tool if needed. Use
shell variables and hidden input so the password and bearer token are not placed
directly in reusable command history.

```bash
BASE_URL="${BASE_URL:-http://127.0.0.1:48081}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@example.test}"

read -r -s -p "Admin password: " ADMIN_PASSWORD; echo
LOGIN_RESPONSE="$(
  jq -n --arg email "$ADMIN_EMAIL" --arg password "$ADMIN_PASSWORD" \
    '{email:$email,password:$password}' \
  | curl -sS -H 'Content-Type: application/json' --data-binary @- \
      "$BASE_URL/api/auth/login"
)"
TOKEN="$(jq -r '.accessToken' <<<"$LOGIN_RESPONSE")"
unset ADMIN_PASSWORD LOGIN_RESPONSE
```

The login request is `POST /api/auth/login` with exactly `email` and `password`.
The response contains `accessToken`. Keep the token in the shell variable and do
not paste it into logs, screenshots, issues or commits.

## Verify the capability catalogue

The capability catalogue below remains capability-only. AI-API-001 under published v0.13.0
also provides a separate internal registry of authorized READ primitives, with no
new public catalogue endpoint. AI invoke grants no business permissions. See
[primitives, published contract and differential evaluation](ai-read-primitives.md).

```bash
curl -sS -H "Authorization: Bearer $TOKEN" \
  "$BASE_URL/api/core/capabilities" \
| jq -r '.items[].id'
```

Confirm that `wms.uoms.list` is present. The current WMS READ catalogue also
contains `wms.storage-attributes.list` and `wms.item-families.list` for an
authorized administrator.

`ai.assistant.invoke` does not grant WMS permissions. Module availability and
capability permissions are re-evaluated when each capability is invoked.

## Create synthetic WMS data

Create two synthetic units of measure through the public WMS API:

```bash
curl -sS -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  --data '{"code":"ZLIVE01","description":"Synthetic Groq validation unit one"}' \
  "$BASE_URL/api/wms/uoms"

curl -sS -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  --data '{"code":"ZLIVE02","description":"Synthetic Groq validation unit two"}' \
  "$BASE_URL/api/wms/uoms"
```

If a code already exists, the API returns `409 wms_uom_code_conflict` and does not
change the existing row. Verify the current catalogue with:

```bash
curl -sS -H "Authorization: Bearer $TOKEN" \
  "$BASE_URL/api/wms/uoms?offset=0&limit=50" \
| jq '{total,items:[.items[]|{code,description}]}'
```

## Run the live assistant test

Send a business-state question and ask the assistant not to invent data:

```bash
curl -sS -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  --data '{"message":"Consulta los datos actuales de ERBAS y dime qué unidades de medida hay registradas. Incluye sus códigos y descripciones y no inventes datos."}' \
  "$BASE_URL/api/ai/assistant/invocations" \
| jq
```

A successful response is HTTP 200 with:

- a nonempty `invocationId`;
- a nonempty `answer`;
- `toolsUsed` containing an entry for `wms.uoms.list`.

The CORE `invocationId` in that entry is distinct from the AI `invocationId`.
Evidence of a successful live path includes an answer containing `ZLIVE01` and
`ZLIVE02` and their synthetic descriptions. Exact model wording is not
contractual.

For a no-tool smoke test:

```bash
curl -sS -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  --data '{"message":"Responde únicamente con la palabra ok, sin usar herramientas."}' \
  "$BASE_URL/api/ai/assistant/invocations" \
| jq
```

The expected result is HTTP 200 with a nonempty `answer` and `toolsUsed: []`.

## Provider tool-generation failures

Tools use semantic names derived from their capability IDs, with dots replaced by
underscores and hyphens preserved (for example `wms_uoms_list`). This avoids opaque
numeric aliases that Qwen was observed to misspell. Capability IDs longer than
the provider's 64-character function-name limit retain a short numeric alias.
Core still resolves and authorizes the original capability ID.

For provider-specific retries, use the
[canonical retry policy](ai-providers.md#groq-retry-policy).
Invalid-argument recovery belongs to the shared assistant loop; it is not a
promise that every provider/model will complete a business question. Compare
[provider/model evidence and limits](ai-providers.md#capability-matrix) before
interpreting a live test result.

## Security

- Use only synthetic or otherwise non-sensitive data for external-provider testing.
- Never commit `.env` or any API key.
- Never put provider keys in Angular configuration or source-controlled client code.
- Never put provider keys in `erbas-contract`.
- Never paste credentials into issues, pull requests, logs or screenshots.
- CI and shared conformance use `DeterministicAiModelClient`; they need no Groq key.
- External-provider disclosure policy for real business data is outside AI-001.

## Cleanup

```bash
./bin/down
```

This stops the local services and preserves database volumes. Do not remove volumes
unless you intentionally want to discard local development data.

## Deterministic usage tests

[Usage metering](ai-usage.md) is internal telemetry, not an addition to the public
assistant response. `AiUsageNormalizationTests` uses synthetic HTTP responses for
Groq/Ollama token fields, IDs, failures, timeout/cancellation and retry attempts.
`AiUsageMeteringTests` uses the existing Testing host and disposable PostgreSQL
validation databases for real persistence, aggregation, tool-dispatch counts,
content exclusion, idempotent concurrent writes and secondary-meter failure.
`AiUsageTests` verifies the existing provider-neutral port and dependency boundary.
No inference service or Internet connection is required by these tests.

After provisioning the repository validation databases with the normal
`bin/validation-db` workflow, the focused integration filter inside the Docker
`app` service is `FullyQualifiedName~AiUsage`. The standard architecture,
integration and `./bin/check` workflows include these tests; `./bin/check` also
checks the AI migration and both usage tables. Do not run a new live-provider
request merely to turn optional metrics into apparent validation evidence.
Historical smoke timings/tool traces do not certify newly persisted usage.
