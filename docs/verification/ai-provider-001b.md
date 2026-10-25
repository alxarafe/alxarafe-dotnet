# AI-PROVIDER-001B — local Ollama integration smoke

For current setup and the cross-provider capability matrix, see the
[canonical AI provider reference](../ai-providers.md). This report remains the
source of record for the detailed historical smoke results below.

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


## 2026-10-25 follow-up: persisted metering

This follow-up started from clean, synchronized `main` at `b1b0df1`, which already
contained AI-PROVIDER-001A, AI-PROVIDER-001B, AI-API-001 and AI-USAGE-001, with
`contract.revision=v0.12.0`. The provider implementation was reused unchanged.
The only code change extends normalization regressions to cover missing usage
for both providers and an optional numeric Ollama reasoning-token counter.

### Reproduction boundary

The existing Docker development image was built with `docker compose build app`.
PostgreSQL was started through Compose. Each smoke lifecycle used a disposable
Development backend from `/app/publish`, `docker run --network host`, an ephemeral
`http://127.0.0.1:PORT` listener, and two dedicated disposable databases
(`ai_ollama_smoke`, `ai_ollama_security_smoke`). Existing development databases,
local `.env`, normal Compose networking and the Ollama listener were unchanged.
The database container retained its bridge network with no published port; its
current IP was supplied only to the disposable backend connection strings.

The configuration was exactly the Ollama configuration above, including the
1800-second completion timeout and explicit `AI_MODEL=gemma4:latest`. No Groq
credential or Groq inference was used. Ollama's version endpoint reported
`0.20.7`. A normal seeded CORE administrator logged in through `/api/auth/login`
and enabled WMS and AI through module administration. The external
`erbas-demo-data/seed.sh` ran through its POSIX-sh entrypoint in a disposable
`python:3.13-alpine` host-network container. Static validation, stock comparison
and the functional oracle passed in each lifecycle (15 items, 15 locations,
20 stock positions, 40 movements; lots were not loaded).

Only the requested questions were sent to the real ERBAS
`POST /api/ai/assistant/invocations`, with `Accept-Language: es`. No direct
completion request, forced SKU, prompt/model-option change or Gemma-specific
logic was used. Public answers and allowlisted persisted usage were retained;
provider response bodies and reasoning content were not captured. Both backend
containers and all four disposable databases were removed successfully.

### Functional observations, including the failure

| Observation | HTTP | Public answer / oracle | Logical model calls | HTTP attempts | Tool attempts / successes | Wall time |
| --- | ---: | --- | ---: | ---: | ---: | ---: |
| Simple | 200 | `OK`, no tools | 1 | 1 | 0 / 0 | 51.067 s |
| Water, first observation | 200 | Failed: information-unavailable answer; no inventory read | 3 | 3 | 2 / 1 | 152.336 s |
| Water, second observation | 200 | Correct A.01.01 = 480 UN and B.01.04 = 120 UN | 3 | 3 | 2 / 2 | 169.644 s |

The first water observation returned:

> No puedo determinarlo con la información que ERBAS pone a mi disposición actualmente.

It attempted `wms.items.list` with `InvalidRequest`, then successfully executed
`wms.item-families.list` and ended without reading inventory. HTTP success and
`Completed` metering describe execution, not semantic answer correctness. This
observation failed the water oracle and is retained as a failure.

A single second water observation was made to investigate variation against the
historical successful trace, with the same question/configuration, a newly loaded
identical dataset and no product change. It is a separate invocation, not a
provider-level retry. Its public answer was:

```text
The registro de Agua X de 1 L se encuentra en los siguientes lugares:

*   **A.01.01** (Almazara de la Rúa) con una cantidad registrada de 480 unidades.
*   **B.01.04** (Almazara de la Rúa) con una cantidad registrada de 120 unidades.
```

The expected location/quantity pairs were present, but the answer also invented
`Almazara de la Rúa` as the warehouse name; the dataset says `Almacén Aljarafe`.
The narrow balance oracle passed; full answer fidelity did not. There was no
rejected call in this second invocation, so successful argument recovery was
not re-certified by the follow-up. The original successful recovery evidence
above remains historical evidence and must not be attributed to these new runs.

The second observation's bounded audit trace was:

```text
1. wms.items.list: Succeeded
2. wms.inventory.list: Succeeded
```

The existing assistant supplies its `invalid_arguments` tool result after a READ
validation rejection; deterministic recovery coverage verifies that feedback and
the unchanged budget. Rejected argument values were not captured, so these
observations do not establish which exact field/value caused a rejection.
Only successful tool invocations appear in public `toolsUsed`. Both water
observations stayed within the unchanged eight-call/eight-turn budget.
The differing answers are evidence of model variability, not universal provider
or model reliability. The initial failure must not be relabeled as success.

### Actually persisted usage

Each row below is one real provider HTTP attempt, read back from
`ai_usage_records`; durations are converted from .NET ticks to seconds.
All effective providers were `ollama`; configured and reported models were both
`gemma4:latest`. All attempts had outcome `Succeeded` and attempt number 1;
there were no provider retries.

| Observation / call | Input tokens | Output tokens | HTTP duration | Safe response ID |
| --- | ---: | ---: | ---: | --- |
| Simple / 1 | 1216 | 2 | 50.924742 s | `chatcmpl-647` |
| Water, first observation / 1 | 1223 | 175 | 17.058089 s | `chatcmpl-673` |
| Water, first observation / 2 | 1299 | 951 | 97.246201 s | `chatcmpl-329` |
| Water, first observation / 3 | 1554 | 330 | 37.855081 s | `chatcmpl-633` |
| Water, second observation / 1 | 1223 | 517 | 107.925644 s | `chatcmpl-371` |
| Water, second observation / 2 | 2123 | 213 | 47.292276 s | `chatcmpl-658` |
| Water, second observation / 3 | 2315 | 81 | 13.988651 s | `chatcmpl-336` |

| Observation | Invocation ID | Persisted assistant duration | Total input / output tokens |
| --- | --- | ---: | ---: |
| Simple | `b6d53e8f39694fcbb9e55e0acea35f50` | 51.012553 s | 1216 / 2 |
| Water, first observation | `adb0026bf58a4d5ba493a21033974455` | 152.314470 s | 4076 / 1456 |
| Water, second observation | `a27c658a2f884296bb48157233df83c2` | 169.567760 s | 5661 / 811 |

The locally certified fields are provider, configured/reported model, safe body
response ID, input/output counters, actual HTTP duration, assistant duration,
logical model calls, HTTP attempt count, tool calls and successful tool calls.
The persisted counters are exactly those available through the compatible
response; output tokens are not split or reclassified by inspecting reasoning.

`reasoning_tokens`, `cached_tokens`, `provider_request_id`, `provider_cost`,
`provider_cost_currency` and `billing_mode` were **null in every attempt**.
No counters were extrapolated. Cost remains null, never zero, even for local
Ollama. Numeric reasoning-token support has synthetic coverage only; this server
and model did not supply a compatible counter. No pricing, locality policy or
billing attribution was introduced.

Reasoning content has no field in the typed completion DTO, public response,
functional audit or usage schema. It is ignored by the common decoder, never
copied to conversation messages and never logged. Synthetic secrecy regressions
cover a known reasoning marker; the second smoke also passed bounded log checks
for credentials, bearer markers and reasoning-field markers. No provider-body
capture was used to collect these metrics.

### Follow-up validation

After both smoke lifecycles, the final checks were run in Docker:

| Check | Result |
| --- | --- |
| Restore and solution build | Passed; zero warnings/errors |
| Focused AI/provider tests | 122 passed |
| Architecture | 132 passed |
| Integration | 370 passed |
| `dotnet format Alxarafe.sln --no-restore --verify-no-changes` | Passed |
| `git diff --check` | Passed |
| `AI_GROQ_API_KEY= AI__Groq__ApiKey= ERBAS_CONTRACT_DIR=<clean-v0.12.0-checkout> ./bin/check` | Passed; published shared contract, local Bruno, persistence, log secrecy and cleanup |

The failed first water oracle and incorrect warehouse label in the second answer
prevent treating this follow-up as an unconditional functional certification.
No new local commit closes the task while these acceptance limitations remain
unresolved. The working changes are limited to the normalization test and the
provider/usage/verification documentation; no new meaningful production literals
or configuration settings were introduced.

No functional changes were made to `AiAssistant`, primitives, capabilities,
authorization/re-evaluation, the eight-call limit, functional audit or the public
v0.12.0 contract. Groq's resolver, Bearer requirement, `tool_use_failed` handling
and bounded `Retry-After` policy remain covered by deterministic regressions;
no live Groq call was made. No push, MR, merge, tag or release was performed.
