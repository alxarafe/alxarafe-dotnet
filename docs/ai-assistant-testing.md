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

Groq and `qwen/qwen3.8-27b` are local backend choices. They are not fields in the
public ERBAS contract, and clients must not select a provider or model.

## Provider, model and protocol

`AI_PROVIDER` selects the backend provider: **Groq (`groq`) or local Ollama
(`ollama`)**. `AI_MODEL` selects a model independently of that provider
name. The selected provider must actually serve that model. Neither setting is
accepted in an ERBAS HTTP request or exposed to the client.

```dotenv
AI_PROVIDER=groq
AI_MODEL=qwen/qwen3.8-27b
AI_GROQ_BASE_URL=https://api.groq.com/openai/v1
AI_GROQ_TIMEOUT_SECONDS=60
```

For local Ollama, explicitly select the model and the URL reachable from the
backend container:

```dotenv
AI_PROVIDER=ollama
AI_MODEL=gemma4:latest
AI_OLLAMA_BASE_URL=http://127.0.0.1:11434/v1
AI_OLLAMA_TIMEOUT_SECONDS=1800
```

That loopback URL requires the local smoke setup below; it cannot reach the host
from an ordinary bridge container. Ollama sends no Authorization header and has
no required API-key setting. Its configurable 1800-second completion timeout
allows bounded waits for local CPU inference; it does not change the eight-call
or eight-turn budget. No model/performance tuning is introduced.

The backend-only Groq credential remains `AI_GROQ_API_KEY`; never put its value in
client configuration or logs. Native .NET configuration uses `AI:Provider` and
`AI:Model`, plus the existing `AI:Groq:ApiKey`, `BaseUrl` and `TimeoutSeconds`.
Ollama uses `AI:Ollama:BaseUrl` and `AI:Ollama:TimeoutSeconds`. Docker Compose maps
the environment variables above to those keys.

Existing configurations continue to work: omitted provider means `groq`, and
`AI_GROQ_MODEL` / native `AI:Groq:Model` remain legacy model aliases. The new
`AI_MODEL` / `AI:Model` takes precedence; otherwise the existing default model
is retained. `bin/configure-ai` writes `AI_PROVIDER` and `AI_MODEL`, migrating an
existing legacy Groq model entry while preserving the key and unrelated settings.
Set `AI_PROVIDER` in the local file before running the helper; it skips the key
prompt for Ollama and preserves the other provider's settings. Ollama reads
`AI:Model` directly; when switching an existing environment, explicitly set
`AI_MODEL` rather than inheriting its former model/default.
No automatic rewrite of local configuration occurs during backend startup.
An unknown provider fails clearly when `IAiModelClient` is resolved. Testing
retains the deterministic adapter and does not resolve a real provider.

The internal flow is `AiAssistant -> IAiModelClient -> AiProviderResolver ->
the selected GroqAiModelClient or OllamaAiModelClient ->
OpenAiCompatibleModelClient -> provider`. The common client owns
chat-completion serialization, messages, tools/results, response parsing,
transport errors/timeouts and the existing lossless tabular encoding. It reads
completion metadata such as finish reason without extending the public result.
Unknown fields, including provider `reasoning`, are ignored; reasoning is never
used as an answer, persisted or recorded in functional audit. Usage is not consumed.
The common client permits explicit no-auth transport and has no retry policy.
Groq still rejects missing/blank credentials and alone retains its bounded
`tool_use_failed` and `Retry-After` recovery and quota-error classification.

A future OpenAI-compatible provider needs its configuration and resolver entry;
it can use the common client directly or specialize its completion method for
provider-specific error/retry rules. This requires no change to `AiAssistant`,
capabilities, primitives or authorization, as demonstrated by the Ollama adapter.
A different wire protocol can implement `IAiModelClient` behind the same resolver.
Only Groq and local Ollama are registered. No streaming,
images, embeddings, Responses API, fallback, per-request/tenant selection,
automatic routing, provider database or administration UI is implemented here.

## Loopback-only Ollama and the local smoke test

The observed Linux installation listens only on `127.0.0.1:11434`. A container
using bridge networking plus `host.docker.internal:host-gateway` received a
connection refusal: the host gateway is not the host loopback interface. Adding
a DNS alias alone does not solve that listener mismatch. Ollama was not rebound
to `0.0.0.0`, and its service configuration was not changed.

The smoke uses a disposable backend container with `docker run --network host`.
Its configured Ollama URL is `http://127.0.0.1:11434/v1`; the ERBAS listener is
explicitly `http://127.0.0.1:EPHEMERAL_PORT`. No published ports are used. Normal
Identity login supplies a disposable administrator, and the existing dataset
loader writes through WMS HTTP APIs into backend-owned disposable databases.
PostgreSQL stays on its private Docker bridge: the temporary backend receives
its current container IP in connection strings, with no PostgreSQL port published.
The native validation image is built before the real smoke. See
[AI-PROVIDER-001B evidence](verification/ai-provider-001b.md).

This is a Linux local smoke arrangement, not a production deployment mechanism.
Host networking shares the host network namespace, bypasses bridge isolation,
discards port publishing rules and loses ordinary Compose service-name discovery.
A future production integration must decide isolation, reachability, bind
addresses, port conflicts, authentication and platform support separately; this
phase does not implement that deployment design. See the official
[Docker host networking documentation](https://docs.docker.com/engine/network/drivers/host/)
and [Ollama local OpenAI-compatible API](https://github.com/ollama/ollama/blob/main/docs/api/openai-compatibility.mdx).

## Configure the backend

The preferred workflow creates or updates the repository-root `.env`, which is
local and ignored by Git:

```bash
./bin/configure-ai
```

The helper is safe to run from any directory. It:

- asks for the Groq API key with hidden input only when Groq is selected;
- shows only whether an existing key is already configured, never its value;
- lets you keep an existing key or replace it intentionally;
- uses existing model, base URL and timeout values as interactive defaults;
- uses the selected supported provider and migrates the legacy Groq model name to `AI_MODEL`;
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

The capability catalogue below remains capability-only. AI-API-001 under published v0.12.0
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
