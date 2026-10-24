# AI-PROVIDER-001B — local Ollama integration smoke

## Scope and implementation

The task branch was based directly on validated AI-PROVIDER-001A commit
`32db657`, by explicit authorization. `main` was not changed or merged.
The resolver now registers `groq` and `ollama`. `OllamaAiModelClient` only supplies
provider configuration to the existing `OpenAiCompatibleModelClient`; no message,
tool/schema, result, completion parser or compaction code was duplicated.

The common boundary now accepts explicit `apiKey: null` to omit authentication.
Groq still supplies a non-null credential value, so its missing/blank-key rejection,
Bearer header and existing bounded error/retry policy remain unchanged. Ollama
uses the common transport with no Authorization header or required key and no
provider-specific retry. `AI_MODEL` remains provider-independent.

`AiAssistant`, `IAiModelClient`, primitives, capabilities, authorization,
execution-time permission checks, functional audit and public v0.12.0 request,
response and provenance semantics needed no functional change. No prompt, tool
budget, model option, tenant/fallback/routing policy or performance setting was
introduced or tuned. The local provider has a configurable bounded completion
timeout, not a performance optimization.

## Network boundary

The host listener was observed at `127.0.0.1:11434` (Ollama 0.20.7).
A bridge container using `host.docker.internal:host-gateway` failed with connection
refusal. The alias resolves a gateway address, not a service bound only to host
loopback. A host-network container reached the same server successfully.
Ollama's bind address and service configuration were not changed; it was not
exposed on `0.0.0.0`.

The smoke used the freshly built `alxarafe-dotnet-app` image and
`docker run --network host --workdir /app/publish ... dotnet
/app/publish/Alxarafe.Host.dll --urls http://127.0.0.1:EPHEMERAL_PORT`.
The backend ran in Development to resolve the real provider, with an ordinary
seeded administrator and normal Identity login. Listener inspection verified
loopback binding. PostgreSQL retained its bridge network with no published
port; the disposable backend received the current PostgreSQL container IP in
connection strings for `alxarafe_test` and `alxarafe_security_test` only.
No host-network mode was added to normal Compose or production configuration.

Host networking removes network-namespace isolation, ignores published-port
mapping and does not retain normal Compose service-name discovery. Bind addresses,
port conflicts, service addressing, provider authentication/isolation and platform
support need a separate production design; this task deliberately does not
resolve that design. See [Docker host networking](https://docs.docker.com/engine/network/drivers/host/).

## Configuration used

```dotenv
AI_PROVIDER=ollama
AI_MODEL=gemma4:latest
AI_OLLAMA_BASE_URL=http://127.0.0.1:11434/v1
AI_OLLAMA_TIMEOUT_SECONDS=1800
```

These values were supplied only to the disposable smoke runtime, not written to
the existing local `.env`. The model/provider/URL values were checked inside the
running container. Groq credentials were overridden with empty values and no
Groq request was made. `bin/configure-ai` supports a preselected Ollama provider
without a key prompt and preserves unrelated Groq settings.

## Deterministic validation before real completions

| Check | Result |
| --- | --- |
| Docker image build, restore and solution build | Passed, zero warnings/errors |
| Focused provider tests | 42 passed |
| Architecture | 130 passed |
| Integration | 343 passed |
| Whole-solution `dotnet format Alxarafe.sln --no-restore --verify-no-changes` in Docker | Passed |
| `git diff --check` | Passed |
| Groq helper regressions and Ollama keyless/preserved-settings fixtures | Seven synthetic cases passed |
| `AI_GROQ_API_KEY= AI__Groq__ApiKey= ERBAS_CONTRACT_DIR=/home/rsanjose/Desarrollo/Alxarafe/erbas-contract ./bin/check` | Passed |

The full check repeated all 130 architecture and 343 integration tests and passed
published v0.12.0 shared conformance, local Bruno, persistence invariants, log
secrecy and validation cleanup. New provider tests cover resolver selection,
URL/model/timeout configuration, no-key/no-header transport, structured tools and
results through the common client, ignored reasoning, and Groq credential
rejection. Existing Groq, permission, revocation and AI-API-001 regressions remain.
No real provider request was needed for deterministic validation.

## Real ERBAS endpoint observations

Both questions were posted once to `/api/ai/assistant/invocations`, with
`Accept-Language: es` and a normal administrator bearer token. No direct
completion request to Ollama was used. The existing system prompt was unchanged,
and no correct SKU was inserted in either user prompt.

| Case | Question | Effective provider/model | HTTP | Public answer | Successful tool calls | Total duration |
| --- | --- | --- | ---: | --- | ---: | ---: |
| Simple | Responde únicamente: OK | ollama / gemma4:latest | 200 | OK | 0 | 50.665 s |
| Water | ¿Dónde está registrada el Agua X de 1 litro? | ollama / gemma4:latest | 200 | A.01.01 = 480 units; B.01.04 = 120 units | 2 | 151.835 s |

The actual water answer was:

```text
El "Agua X 1 L" (SKU: AGUA-X-1L) está registrado en el almacén ALJARAFE-01 en las siguientes ubicaciones:

*   **B.01.04:** 120 unidades
*   **A.01.01:** 480 unidades
```

The semantic water oracle passed: both locations and balances are correct, no
false location/quantity was present, and an explicit total was not required.
The existing independent WMS dataset loader passed static, stock and functional
oracle checks before the water question; it wrote synthetic data through normal
WMS HTTP APIs, with no direct business-data SQL writes.

The water audit records three attempted tool calls in this order:

1. `wms.items.list`: `InvalidRequest`, counted against the existing budget.
2. `wms.items.list`: succeeded; offset 0, limit 10, 10 returned / 15 total.
3. `wms.inventory.list`: succeeded; offset 0, limit 50, 2 returned / 2 total.

The existing argument-recovery loop corrected the rejected input without prompt
changes. Exact rejected arguments were not logged and are not inferred here.
Only the two successful calls appear in public `toolsUsed`, in order, with unique
invocation IDs. Four model completions returned HTTP 200, with no provider retry.
This stayed within the unchanged eight-call/eight-turn budget.

The nonstreaming API produced its first HTTP response after approximately
50.660 s for simple and 151.830 s for water;
those times are effectively the final-result times, not token-level TTFT.
Timestamped bounded audit metadata places the first water attempt at about
15.8 s and the first successful primitive result at about
62.2 s. No streaming instrumentation or model benchmarking was added.

The first host completed the simple case, then its dataset launch failed because
the pinned Python image has no Bash executable. That infrastructure error occurred
before the water invocation. The host/databases were cleaned up. The harness was
corrected to execute the loader's existing POSIX-sh entrypoint in a fresh
disposable host/database, and only the water question was then sent. The successful
simple invocation was preserved and not repeated. No product-code change or
model/prompt adjustment was required for this harness correction.

Both smoke lifecycles passed log secrecy and container/database cleanup. No
provider `reasoning`, chain-of-thought, raw provider response or full system
prompt was exposed or retained in the report/audit. The saved outputs are only
public endpoint answers and bounded operational metadata. These are local API
smoke observations, not a benchmark or full-stack UI verification.
