# Normalized AI usage metering

AI-USAGE-001 adds internal technical usage telemetry, not Budget Guard, quotas,
pricing, invoicing or a billable ledger. The public `erbas-contract v0.12.0`
assistant request/response and `toolsUsed` semantics are unchanged. No usage,
provider IDs or billing mode are added to the public response.

## Boundary and record granularity

`AiAssistant` consumes the existing provider-neutral `IAiModelClient` port.
`AiProviderResolver` returns that same port. `AiModelResponse` retains answer/tool
calls and optionally carries `Executions`: normalized `AiUsageRecord` values.
Transport compatibility remains an Infrastructure detail, not an Application
contract. Typed model failures/cancellation can also carry these records.

The assistant passes an `AiUsageBatch` to the separate `IAiUsageMeter` port:

- One `AiInvocationUsage` summary per assistant invocation: server invocation ID,
  authenticated user UUID when reliably resolved, start, wall duration, outcome,
  logical model calls, dispatched tool calls and successful tool calls.
- One `AiUsageRecord` per actual HTTP provider attempt, including internal retry
  attempts: immutable event UUID, invocation ID, logical model-call UUID, attempt
  sequence, provider, configured model, optionally reported model, start,
  duration, outcome, optional token counters and provider identifiers.
- Nullable provider-reported cost/currency and billing mode are representable,
  but **both current adapters leave them null**. No tariff lookup or estimation
  exists. No prompt, answer, tool arguments/results, headers, credential or
  reasoning content is part of either persisted shape.

The deterministic Testing adapter reports no provider attempts; the assistant
summary still counts logical calls. Missing telemetry is not fabricated.

## Metrics and semantics

| Metric | Meaning |
| --- | --- |
| `InputTokens` | Nonnegative integer `usage.prompt_tokens`, if supplied |
| `OutputTokens` | Nonnegative integer `usage.completion_tokens`, if supplied |
| `ReasoningTokens` | Optional `usage.completion_tokens_details.reasoning_tokens`; never inferred from reasoning text |
| `CachedTokens` | Optional `usage.prompt_tokens_details.cached_tokens`; never inferred from latency or cache configuration |
| Attempt `Duration` | Elapsed HTTP send through buffered response receipt, including failed transport/timeouts; excludes retry delay and later parsing/tool execution |
| Invocation `Duration` | Assistant wall time including discovery, all model calls/retry delays and tools; excludes the final metering write |
| `ModelCalls` | Calls to `IAiModelClient.CompleteAsync`; one can produce multiple HTTP attempts |
| `ToolCalls` | Actual dispatches to the primitive/capability invoker, including rejected/failed invocations; not definitions offered or proposed calls rejected before dispatch |
| `SuccessfulToolCalls` | Successful invocations already represented by the existing ordered `toolsUsed` |
| `ProviderRequestId` | Groq safe `x_groq.id`, otherwise safe single `x-request-id` header; optional |
| `ProviderResponseId` | Safe completion body `id`, distinct from request ID and ERBAS invocation ID |
| `ProviderCost` / currency | Reserved for a reliable provider-reported amount and its unit; currently null, never an externally calculated estimate |

Identifiers from provider responses and the reported model are bounded to 256
ASCII identifier characters (letters/digits, `-_:/.`); unsafe values are omitted.
Malformed, negative, fractional, overflowing or absent token fields remain null.
Unknown fields, including `reasoning`, are ignored. Numeric reasoning-token
counts do not expose chain-of-thought content. No TTFT/streaming measurement exists.

`AiUsageTotals.From` deduplicates immutable event IDs and sums actual attempts,
including failed attempts with reported usage. A token total is available only
when **every** distinct attempt reports that metric; empty/partial/overflowing
sums remain null, rather than becoming a misleading zero or lower bound.
Cached/reasoning counters are details, not extra tokens added to input/output.
`ProviderDuration` sums attempt durations; it is not invocation wall time.

## Provider differences and evidence

Groq and Ollama reuse the whitelist decoder in `OpenAiCompatibleModelClient`.
Groq retains its unchanged generation/rate-limit retry policy and Bearer auth.
Each actual retry has a new event ID and the same logical model-call ID, with an
increasing attempt number. A retry is consumed work, not duplicate persistence.
Ollama still sends no API key and has no provider-level retry policy.

Groq's [prompt-cache documentation](https://console.groq.com/docs/prompt-caching)
describes input/output usage and optional cached-token details. Reasoning counts
are captured only if present in the actual chat response. Ollama's
[OpenAI compatibility implementation](https://github.com/ollama/ollama/blob/main/openai/openai.go)
contains prompt/completion counters and optional cached details; it does not
establish reasoning-token availability for every version/model. Native Ollama
`/api` timing/token fields are not consumed by this `/v1/chat/completions` adapter.
No mapping from an arbitrary `cost` field is assumed.

AI-USAGE-001 originally supplied synthetic normalization, retry and persistence
evidence. The subsequent [Ollama follow-up](verification/ai-provider-001b.md#2026-10-25-follow-up-persisted-metering)
certifies persisted input/output counters, configured/reported model, safe response
IDs and execution timings/counts for local Ollama 0.20.7 with `gemma4:latest`,
including a failed functional water observation. Reasoning/cache counters,
provider request ID, cost/currency and billing mode remained null. Historical
[Groq](verification/ai-api-001.md) and the original Ollama smoke predate metering;
no new Groq inference was performed. See the [provider matrix](ai-providers.md#capability-matrix).
Local Ollama has no provider invoice per token in the tested installation;
provider cost stays null, not zero. Hardware, power and operations cost are not
measured. Other models/versions may supply different optional metadata.

## Persistence, attribution and idempotency

AI.Persistence implements `IAiUsageMeter` with PostgreSQL EF Core, separately from
HTTP Infrastructure and Application. It uses the existing `Platform` connection,
its own `__AiMigrationsHistory`, and migration `202610250001_UsageMetering`.
The AI module applies it during initialization, following existing module
conventions. No new connection variable is needed. Tests use real isolated
PostgreSQL databases, not a substitute database engine.

`ai_usage_invocations` holds summaries; `ai_usage_records` holds attempts linked
to the summary. Durations are stored as ticks; timestamps are UTC PostgreSQL
`timestamptz` (microsecond precision). Event UUID primary keys and the unique
`(invocation_id, model_call_id, attempt)` key prevent repeated writes of the same
batch/attempt. Writes are parameterized and transactional, using conflict-safe
inserts. Concurrent repetition is idempotent. Vendor IDs are not deduplication
keys: distinct actual calls may legitimately return the same vendor identifier.

Only the authenticated user UUID is attributable today, after it matches the
invocation subject. There is no tenant/account/installation/reseller dimension
added speculatively, no conversation copy, and no new usage query endpoint.
[Prepared tenant provider settings](ai-providers.md#tenant-overrides-prepared-not-active-in-runtime)
do not activate provider overrides. CORE-TENANT-001 establishes authoritative
membership context, but tenant usage attribution remains deferred to
AI-PROVIDER-002B. Global effective provider/configured model continue to be
metered by the common transport. A reported provider model never replaces the
configured model.
There is no cross-database identity foreign key. Existing authorization and
functional audit remain authoritative and unchanged; telemetry grants no access.

`AiBillingMode` can represent `Managed`, `Byok` and `Local`. There is currently no
trusted ownership/deployment configuration to distinguish these at runtime:
all current records leave it null, including Ollama. A provider name, URL or key
presence does not reliably establish ownership or locality. Future trusted
configuration can explicitly resolve these values; no heuristic or commercial
hierarchy is introduced here.

A new HTTP invocation receives a new server ID and represents new work; this
feature adds no HTTP idempotency contract and never hashes conversation content.
Multiple model calls in one assistant invocation aggregate by its invocation ID.

## Errors and limits

Non-success responses with valid usage retain those counters and a failed
outcome. Transport failures/timeouts/cancellation retain attempt duration and
unknown token usage. A completion parsing failure marks the attempt failed even
when the HTTP status was successful. Failure before transport creates no invented
HTTP attempt. Unreported consumption remains unknown.

The assistant records once on exit, including failure/cancellation, with an
independent five-second write deadline. Runtime metering is best effort: failures
must not replace the functional answer/error. The persistent adapter emits only
a bounded warning containing the server invocation ID, without exception bodies.
An outer guard also protects against a faulty meter implementation. Schema
initialization remains startup-critical, consistent with other module migrations.

Process crashes, unavailable persistence or a write deadline can lose telemetry;
there is no outbox, durable retry queue, retention policy or reconciliation yet.
This is not suitable as a complete financial ledger. Provider-side work with no
returned usage cannot be reconstructed. Public audit/error behavior, prompts,
tool budgets, primitives, capabilities and permission reevaluation are unchanged.
