# AI providers: configuration, capabilities and validation

This is the canonical reference for provider/model configuration and the scope
of ERBAS provider support. The public API remains `erbas-contract v0.12.0`.
[Local assistant testing](ai-assistant-testing.md) covers login, synthetic data
and deliberate end-to-end requests. Real-provider observations are distinct from
deterministic repository/conformance validation and do not guarantee answer quality.

## Provider and model are separate

Select the provider explicitly; a model name never selects or changes a provider:

```dotenv
AI_PROVIDER=groq
AI_MODEL=<compatible-model>
```

```dotenv
AI_PROVIDER=ollama
AI_MODEL=gemma4:latest
```

Only lowercase `groq` and `ollama` are registered. For compatibility, an omitted
provider defaults to `groq`; this is a fixed default, not inference from the model.
An unknown effective provider fails before any provider HTTP request. Review both
global settings when changing provider: the selected service must actually serve
the chosen model and support the tools needed for the intended task. No model
discovery, fallback, public per-request selection or automatic routing exists.
Tenant overrides are active through authoritative tenancy as described below.
Testing uses the existing deterministic adapter without resolving a real provider.

Root `.env` names below are read by Docker Compose and `bin/configure-ai`.
Native .NET configuration uses `AI:Provider` / `AI:Model` (environment variables
`AI__Provider` / `AI__Model`) and the provider-specific keys shown below. Compose shell-environment values may override
the `.env` file; the helper reads the file rather than those shell overrides.
Keep them consistent when applying configuration. Neither provider nor model is
accepted in the public assistant request.

### Model defaults and compatibility

There are different configuration layers; do not rely on their defaults when
switching providers:

| Layer | Model selection when no explicit model is supplied |
| --- | --- |
| Groq adapter | `AI:Model`, then legacy `AI:Groq:Model`, then `qwen/qwen3.8-27b`; fallback applies to absent native values |
| Ollama adapter | `AI:Model`, then `gemma4:latest`; no native Groq alias lookup |
| Docker Compose `app` | Nonempty `AI_MODEL`, then legacy nonempty `AI_GROQ_MODEL`, then `qwen/qwen3.8-27b`, regardless of provider |
| `bin/configure-ai` | Preserves a nonempty `AI_MODEL`; otherwise uses the legacy model only for Groq, then the selected provider's default |

In particular, setting only `AI_PROVIDER=ollama` does not replace a model already
present in `.env`, and Compose's compatibility fallback is still the Groq default.
Always set `AI_MODEL=gemma4:latest` explicitly for the recorded Ollama case.
Defaults are shipped configuration values, not promises of model availability,
quota or tool compatibility. The legacy Groq model alias remains supported; the
helper migrates it to `AI_MODEL` only when Groq is selected.

## Tenant overrides active at runtime

AI-PROVIDER-002B activates provider/model settings using the authoritative scoped
`TenantContext` supplied by the Host. Authenticated membership resolution selects
the canonical Tenant; AI never derives identity from email, username, host, headers,
query parameters or prompt content. Without a resolved tenant, execution uses global
configuration and does not read tenant settings.

Platform's `tenant_ai_settings.canonical_tenant_id` uniquely references the canonical
Tenant. The legacy `tenant_id` remains compatibility data only: unlinked legacy rows
are ignored, even when their string matches a canonical ID. Linking requires explicit
controlled persistence; no selector, public settings endpoint or administrative UI is added.

| Tenant settings | Effective provider | Effective configured model |
| --- | --- | --- |
| None | Global provider | Global model |
| Provider and model | Tenant provider | Tenant model |
| Provider only | Tenant provider | Global model |
| Model only | Global provider | Tenant model |

Null, empty and whitespace overrides inherit independently. Global `AI_PROVIDER`,
`AI_MODEL`, provider-specific configuration and the historical Groq model alias
retain their existing behavior. Unknown effective provider IDs fail before HTTP;
there is no normalization, repair, automatic fallback or routing.

The scoped `EffectiveAiConfigurationResolver` reads Platform through a fresh EF
context. Production uses `ConfiguredAiModelClient` to await this lookup and freeze
provider/model for the scope's completions. No mutable tenant settings are cached
globally. A later scope reads updates without restart. A settings read failure is
not converted into global fallback. Concurrent scopes have
independent clients and selections. Provider clients do not resolve tenancy.
Membership revalidation retains the existing context-change rejection for business
execution, so tools cannot silently switch to another tenant during an invocation.

Usage summaries capture authoritative canonical tenant identity at invocation start,
while attempts link to that summary. User attribution, effective provider/configured
model and separate reported model are preserved on success and failure. Historical
and non-tenant usage stays nullable; cost/currency/billing mode remain nullable.
Credentials continue to use existing provider configuration, never tenant settings.

AI is one module consuming the tenancy architecture, not a privileged Core concept.
Selection grants no membership, assistant permission, capability or primitive access.
The existing authorization and business database isolation rules are unchanged.
Provider/model selection is not a data-processing security policy.

Still deferred: generic module configuration, Partner/reseller inheritance, BYOK,
tenant-specific credentials, LOCAL_ONLY/CLOUD_ALLOWED, data classification/residency,
provider execution-location policy, automatic routing/fallback (including cost-based
routing), and administrative UI. No new real-provider certification is implied;
deterministic acceptance uses synthetic HTTP and PostgreSQL.

## Groq configuration

| Variable | Requirement | Default / behavior | Native key |
| --- | --- | --- | --- |
| `AI_PROVIDER` | Set explicitly for new configurations | `groq` is also the compatibility default | `AI:Provider` |
| `AI_MODEL` | Set explicitly when selecting/changing a model | Defaults/alias precedence above | `AI:Model` |
| `AI_GROQ_API_KEY` | Required for Groq completion requests | No credential default; missing/blank values fail before transport; Bearer authentication | `AI:Groq:ApiKey` |
| `AI_GROQ_BASE_URL` | Optional | `https://api.groq.com/openai/v1` | `AI:Groq:BaseUrl` |
| `AI_GROQ_TIMEOUT_SECONDS` | Optional, positive integer seconds | `60`, per HTTP completion request | `AI:Groq:TimeoutSeconds` |
| `AI_GROQ_MODEL` | Optional legacy alias | No separate value by default; lower priority than `AI_MODEL` | `AI:Groq:Model` |

Enter the key through `./bin/configure-ai` with hidden input. It never belongs in
client configuration, a public request, source control or logs. The helper can
retain an existing key without displaying it; it does not validate the credential
against Groq or guarantee a model/account quota.

### Groq retry policy

Retries are internal, **not configurable environment settings**:

- With tools offered, HTTP 400 `tool_use_failed` retries only the current
  completion once with temperature zero. Previously completed tool results are
  retained; successful reads are not replayed by this retry.
- HTTP 429 or 503 can retry the current completion once only with an explicit
  `Retry-After` delay/date whose resulting delay is between 0 and 60 seconds.
- Generation and provider retries have separate one-retry budgets; at most three
  HTTP attempts can occur for a completion when both apply. Other 5xx responses
  are unavailable errors without that retry; 413 is not retried by this policy.
- Caller cancellation is preserved; transport timeouts are mapped to the existing
  safe unavailable outcome. This policy does not guarantee eventual success.

This transport policy differs from invalid-tool-argument recovery, which is owned
by the unchanged assistant loop and consumes the existing eight-call/eight-turn
budget. No retry-count, backoff or temperature setting is exposed to operators.

## Ollama configuration

The validated local configuration is:

```dotenv
AI_PROVIDER=ollama
AI_MODEL=gemma4:latest
AI_OLLAMA_BASE_URL=http://127.0.0.1:11434/v1
AI_OLLAMA_TIMEOUT_SECONDS=1800
```

| Variable | Requirement | Default / behavior | Native key |
| --- | --- | --- | --- |
| `AI_PROVIDER` | Set to `ollama` | Omission selects Groq, not Ollama | `AI:Provider` |
| `AI_MODEL` | Set explicitly in Compose and when switching | Native adapter/helper fallback is `gemma4:latest`; see layer differences above | `AI:Model` |
| `AI_OLLAMA_BASE_URL` | Optional if the default is reachable from the backend | `http://127.0.0.1:11434/v1` | `AI:Ollama:BaseUrl` |
| `AI_OLLAMA_TIMEOUT_SECONDS` | Optional, positive integer seconds | `1800`, per HTTP completion request | `AI:Ollama:TimeoutSeconds` |

The adapter uses Ollama's OpenAI-compatible `/v1/chat/completions` route through
the existing shared client. Endpoint and model are configurable. Local Ollama
requires no API key: this adapter sends no Authorization header, and there is no
Ollama API-key variable. ERBAS still requires the normal authenticated and
authorized caller; keyless provider transport grants no business permissions.
A configurable URL does not imply support for an
authenticated remote Ollama service. No provider-level retry policy is implemented
for Ollama; common assistant argument recovery still applies.

Ollama can run outside Docker, as it did on the host in the recorded smoke. ERBAS
does not install/start Ollama, download models or implement provider health checks.
The model must already be available on the configured service.

### Docker reachability and deployment boundary

`127.0.0.1` in a normal bridge container refers to that container, not the host.
The recorded host Ollama listener was restricted to `127.0.0.1:11434`; adding
`host.docker.internal:host-gateway` produced a connection refusal because a
host-gateway address is not the host loopback listener.

The successful smoke used a disposable Linux backend container with `--network
host` and its own ERBAS listener explicitly bound to a loopback ephemeral port.
Ollama was not rebound to `0.0.0.0`. This was **verification only**, not a normal
Compose or production deployment design. Host networking shares the host network
namespace, ignores published-port mapping and changes service-name reachability;
port conflicts, bind exposure, isolation and authentication remain deployment
questions. Normal Docker-to-host Ollama deployment is still pending design.
Do not assume `./bin/up` alone makes this loopback URL reachable. The exact
network experiment and cleanup remain in [AI-PROVIDER-001B](verification/ai-provider-001b.md#network-boundary).

## Configure or switch provider/model

1. On first setup only, copy `.env.example` to the ignored root `.env` and set
   permissions to `600`. For an existing setup, edit the existing file.
2. Edit **both** `AI_PROVIDER` and `AI_MODEL` using one of the examples above.
   The helper reads the provider from the file; it does not prompt for a provider
   or infer one from the model. Set a reachable provider URL where necessary.
3. Run the canonical helper from the repository:

   ```bash
   ./bin/configure-ai
   ```

   For Groq, retain/replace the key through hidden input and select the model,
   base URL and timeout. For Ollama, there is no key prompt: select its model,
   base URL and timeout. Existing nonempty model/endpoint/timeout values appear
   as defaults; provider changes do not silently replace the generic model.
4. To switch back, edit `AI_PROVIDER=groq` and `AI_MODEL=<compatible-model>`, then
   run the same helper. Groq-specific settings are preserved while configuring
   Ollama, and vice versa for unrelated settings. Only the selected Groq branch
   migrates its legacy model entry. The helper preserves unrelated comments,
   requires an ignored/untracked `.env` and writes it with mode `600`.
5. Apply the saved configuration through the usual deployment lifecycle. For a
   normal, reachable Compose setup, `./bin/up` rebuilds/starts the services and
   applies environment changes. The Ollama loopback-only smoke is the separate
   verification arrangement described above; it is not a production recipe.

The helper only writes local configuration: it does not start services, make a
provider request, test model compatibility or reload an existing container.
Its model/URL/timeout prompts and saved-file message show what was chosen on
disk. Review only the non-secret provider/model entries in the local editor;
do not dump `.env` or expanded Compose configuration to inspect credentials.
There is **no dedicated ERBAS command or endpoint for active provider/model
status**. A saved file is not proof of the running process's configuration.
The existing `/health` endpoint is process liveness, not provider/model readiness.
For deliberate end-to-end verification, use the existing
[assistant testing workflow](ai-assistant-testing.md), after establishing network
reachability. No new diagnostic command is implied here.

## Capability matrix

States apply to the **ERBAS integration**, not every feature the external service
might offer. Each row has a primary state; its notes define the exact scope.

| State | Meaning |
| --- | --- |
| `SUPPORTED` | Explicit ERBAS implementation exists; this alone does not imply a real-provider smoke |
| `VALIDATED` | Identifiable real-provider evidence exists for the stated model/scenario; not a guarantee for other models or repeated runs |
| `PARTIAL` | Support or its usable/verified scope is incomplete or conditional |
| `NOT_SUPPORTED` | ERBAS does not implement/expose the capability, or the described integration is incompatible |
| `NOT_TESTED` | It may be possible, but sufficient ERBAS evidence is absent; no claim of successful operation |

Evidence **G** is the historical [Groq observation report](verification/ai-api-001.md#closed-provider-observation-phase),
including Qwen and `openai/gpt-oss-20b` successes, failures and variability. Those
observations predate the shared-transport extraction; subsequent Groq regressions
are deterministic, not a new post-extraction real-provider certification.
Evidence **O** is [AI-PROVIDER-001B](verification/ai-provider-001b.md#real-erbas-endpoint-observations)
with local Ollama 0.20.7 and `gemma4:latest`. Its
[metering follow-up](verification/ai-provider-001b.md#2026-10-25-follow-up-persisted-metering)
adds real persisted counters and retains a failed water answer and an invented
warehouse label. Deterministic transport/resolver
coverage is in the linked [provider tests](../tests/Alxarafe.Host.IntegrationTests/).

| ERBAS capability / evidence scope | Groq | Ollama |
| --- | --- | --- |
| Basic chat, specifically a tool-free answer | `SUPPORTED`: adapter and deterministic direct-answer coverage; G's short direct probe exhausted its token allowance, not a successful tool-free smoke | `VALIDATED`: O returned `OK`, no tools |
| Structured tool calling and final answer | `VALIDATED`: G contains successful ERP reads with Qwen/GPT-OSS; the full historical matrix was not green | `VALIDATED`: O completed items/inventory and the correct water answer, with `gemma4:latest` |
| Recovery after invalid tool arguments | `SUPPORTED`: shared assistant recovery and deterministic regressions; G does not establish an equivalent per-invocation rejection/correction trace | `VALIDATED`: O records one rejection followed by two successful primitive calls |
| Authentication / API key mode | `VALIDATED`: authenticated Groq requests in G; required Bearer credential, with synthetic header/rejection tests | `VALIDATED`: O used local keyless transport; synthetic tests verify absent Authorization header |
| Explicit model selection | `VALIDATED`: G changed between named Qwen and GPT-OSS models; arbitrary model compatibility is not implied | `VALIDATED`: O explicitly selected `gemma4:latest`; alternative Ollama models are `NOT_TESTED` here |
| Configurable endpoint | `SUPPORTED`: base URL configuration and synthetic custom-endpoint coverage | `SUPPORTED`: base URL configuration/custom-endpoint tests; O used the stated local endpoint only |
| Configurable completion timeout | `SUPPORTED`: 60 s default; configuration and synthetic timeout mapping tested, not a universal live deadline guarantee | `SUPPORTED`: 1800 s default; setter tested and used by O; expiry not exercised in that smoke |
| Automatic provider-level retries | `VALIDATED`: G records generation and rate-limit recovery; internal bounds also have deterministic tests | `NOT_SUPPORTED`: no Ollama retry policy; argument correction above is a different mechanism |
| Streaming public answers | `NOT_SUPPORTED`: current endpoint returns a complete JSON response | `NOT_SUPPORTED`: O was nonstreaming; no ERBAS streaming interface |
| Normalized input-token metering | `SUPPORTED`: optional chat usage, synthetic coverage | `VALIDATED`: follow-up persisted compatible prompt counters for the three identified invocations |
| Normalized output-token metering | `SUPPORTED`: optional completion counter | `VALIDATED`: follow-up persisted compatible completion counters |
| Normalized reasoning-token metering | `PARTIAL`: optional numeric details when supplied; no new live evidence | `PARTIAL`: numeric decoder has synthetic coverage; counter null in the follow-up; reasoning content ignored |
| Normalized cached-token metering | `SUPPORTED`: optional cached-token details | `PARTIAL`: optional decoder; cache counter null in every follow-up attempt |
| Request/provider ID capture | `SUPPORTED`: optional safe x_groq/header request ID and body response ID | `VALIDATED`: safe body response IDs persisted in the follow-up; header request IDs remained null |
| Monetary cost metering | `NOT_SUPPORTED`: no pricing, billing or normalized cost calculation | `NOT_SUPPORTED`: no cost calculation; this local service is not billed per token |
| End-to-end execution without Internet | `NOT_SUPPORTED`: the configured hosted Groq service requires network access; synthetic local fixtures are not offline Groq | `NOT_TESTED`: local inference succeeded, but no controlled Internet-disconnected ERBAS run was recorded |
| Use from Docker | `VALIDATED`: G used backend-owned Docker validation hosts | `PARTIAL`: O validates temporary host networking; normal bridge-to-host deployment remains unresolved |
| Real smoke evidence as a whole | `PARTIAL`: G preserves successes, quota/transport failures, changing outcomes and a final unobservable invocation | `VALIDATED`: O's two identified API cases passed; not provider-wide or all-model equivalence |

Token rows concern internal accounting, not model context sizes or public token-limit
settings. AI-USAGE-001 adds nullable normalized counters and PostgreSQL records;
Groq metrics have synthetic coverage; the linked Ollama follow-up identifies
exactly which metrics were persisted locally.
See [usage metering](ai-usage.md) for exact fields, duration/tool semantics,
aggregation, idempotency, failure behavior and unresolved billing attribution.
ERBAS invocation/correlation IDs remain distinct from provider identifiers.
Finish reason is present in a private wire DTO but is not a public normalized
result field. Provider `reasoning` and unknown metadata are not public outputs,
functional audit content or persisted conversation state.

## Known limitations and observed Ollama results

- The ERBAS API is nonstreaming. Token-level TTFT was not measured; first HTTP
  response time is effectively the final answer time, not first-token latency.
- Ollama `reasoning` was neither recorded nor exposed in O. Ignoring that field
  is not reasoning-token metering or a reasoning-content API.
- Normalized tokens/IDs are optional; the follow-up validates Ollama input/output
  counters and body response IDs only for its identified invocations. Provider cost
  remains null: no pricing or cost calculation exists. Local Ollama is not an
  invoiced per-token service in this installation; hardware, power and operations
  cost are not measured. Billing ownership/locality remains unresolved at runtime.
- Local latency depends on model, hardware, load and model residency. O used CPU
  inference and was not a performance benchmark. No tuning or TTFT instrumentation
  was introduced.
- Normal Docker-to-Ollama deployment still needs a network/isolation design.
  The temporary host-network smoke did not broaden the Ollama listener.
- One `gemma4:latest` smoke does not establish tool support for all Ollama models
  or future contents of that model tag. Other models/installs need scoped evidence.
- Groq and Ollama are not functionally equivalent merely because they share a
  transport. G includes quality/quota failures and an unresolved observation;
  neither those failures nor O's successes may be rewritten as universal parity.
- Configuration changes do not install a provider/model, guarantee quotas or
  answer correctness, or create a provider-status endpoint.

The concise O result summary is retained here for discovery only:

| Case | Observed result |
| --- | --- |
| Simple | HTTP 200; answer `OK`; approximately 50.7 s; 0 tools |
| Agua X | HTTP 200; approximately 151.8 s; 2 correct tools, 3 tool attempts in total; first successful primitive approximately 62.2 s |

The [detailed Ollama report](verification/ai-provider-001b.md) remains the canonical
source for the actual water answer, call ordering, argument recovery, timings,
loader preparation error and cleanup. This reference does not replace or relabel
that evidence, and no new real-provider test is implied by its publication.

## Extending support

A new provider requires **implementation + tests + configuration + capability
matrix + known limitations + smoke evidence when viable**. Reuse the model port
and common transport where applicable; keep provider-specific policy separate.
Document every observable difference, limitation, configuration requirement and
unvalidated capability by provider and, where relevant, model. Provider selection
cannot expand effective tools or bypass authorization. See the
[architectural rule](architecture.md#ai-assistant).

Implementation references: [resolver](../src/Modules/Ai/Alxarafe.Modules.Ai.Infrastructure/AiProviderResolver.cs),
[Groq adapter](../src/Modules/Ai/Alxarafe.Modules.Ai.Infrastructure/GroqAiModelClient.cs),
[Ollama adapter](../src/Modules/Ai/Alxarafe.Modules.Ai.Infrastructure/OllamaAiModelClient.cs),
[shared client](../src/Modules/Ai/Alxarafe.Modules.Ai.Infrastructure/OpenAiCompatibleModelClient.cs),
[Compose mapping](../docker-compose.yml), [template](../.env.example) and
[configuration helper](../bin/configure-ai).
