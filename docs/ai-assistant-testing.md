# Local AI assistant testing

This guide configures a real AI provider for local Development and verifies the
AI-001 assistant end to end. It is intended for developers, evaluators and
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
     | backend-only provider credential
     v
    Groq
```

| Repository | Responsibility |
| --- | --- |
| `erbas-contract` | Defines the provider-neutral public HTTP contract. It never stores provider credentials. |
| `alxarafe-dotnet` | Runs the backend and owns the local AI provider configuration. |
| `erbas-client` | Talks only to ERBAS. It must never receive the Groq API key. |

The browser must never call Groq directly. The API key belongs only to the backend
runtime, normally as a local `.env` value consumed by Docker Compose.

Groq and `qwen/qwen3.8-27b` are local backend choices. They are not fields in the
public ERBAS contract, and clients must not select a provider or model.

## Configure the backend

The preferred workflow creates or updates the repository-root `.env`, which is
local and ignored by Git:

```bash
./bin/configure-ai
```

The helper is safe to run from any directory. It:

- asks for the Groq API key with hidden input;
- shows only whether an existing key is already configured, never its value;
- lets you keep an existing key or replace it intentionally;
- uses existing model, base URL and timeout values as interactive defaults;
- preserves unrelated `.env` variables and comments;
- sets `.env` permissions to `600`;
- refuses to write the key if `.env` is not ignored or is tracked by Git.

It does not start Docker, call Groq, authenticate, create data or modify databases.

A manual alternative is also available:

```bash
cp .env.example .env
chmod 600 .env
```

Then edit `.env` locally. Never commit the populated file. `.env.example` is a
non-secret template and is not loaded automatically by Docker Compose.

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

The capability catalogue below remains capability-only. The AI-API-001 candidate
also provides a separate internal registry of authorized READ primitives, with no
new public catalogue endpoint. AI invoke grants no business permissions. See
[primitives, candidate contract and differential evaluation](ai-read-primitives.md).

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

Groq can reject an invalid model-generated tool call with HTTP 400 and
`tool_use_failed`, for example when the generated function name is not among the
supplied tools. The backend retries that completion once with temperature zero.
It preserves the messages, tool definitions and any completed Core results; it
never executes rejected tool calls or repeats completed capabilities as part of
this retry. This also applies to a completion after a tool result.

Other provider errors are not retried by this mechanism. A repeated generation
failure still returns the safe contractual `500 assistant_execution_failed`;
provider error bodies and failed generations are never exposed to the client.
This bounded recovery does not guarantee that an external model will always
produce a valid tool call.

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
