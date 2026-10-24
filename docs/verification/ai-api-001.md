# AI-API-001 verification

This report preserves local candidate verification and real-provider observations
from 2026-10-24. During those historical runs, v0.12.0 was unpublished and the
backend retained its v0.11.1 pin. The final published v0.12.0 adoption is recorded
separately below; historical results and provider limitations remain unchanged.

## Previously completed automated checks

- Contract `./bin/check-fast`: passed.
- Contract `./bin/check-scope ai`: passed, 30 tests.
- Contract `./bin/check`: passed, 208 tests.
- Candidate shared HTTP conformance against a fresh .NET validation host:
  passed.
- .NET `ERBAS_CONTRACT_DIR=/tmp/ai-api/contract-v0.11.1 ./bin/check`:
  passed; 130 architecture tests, 307 integration tests, platform Bruno,
  PostgreSQL persistence, secret-log and cleanup checks.
- .NET changed-file `dotnet format --verify-no-changes`: passed.
- The integration tests cover effective admin/user tool filtering, persisted
  permission revocation after discovery, execution-time reauthorization, real
  inventory/movement reads, closed input schemas, provenance, and the 8-call
  limit.

## Previous dataset and live-provider matrix

The external `erbas-demo-data/seed.sh` ran twice against a fresh validation
database: first `loaded`, then `already_loaded`. Both runs passed stock and
functional-oracle validation (15 locations, 15 items, 40 movements, 20 stock
positions). No direct database writes were made by the loader.

The previous real Groq matrix did **not** pass completely. The runner preserved
public response summaries and the backend's bounded audit metadata, verified
secret-free logs, and cleaned up its validation container and databases.

| Question | Identity | Result | Primitive calls |
| --- | --- | --- | ---: |
| Water location/quantity | admin | HTTP 500; no usable answer | 0 public successes |
| Water location/quantity | user | Correct: A.01.01 480 UN, B.01.04 120 UN, total 600 UN | 4 |
| Last rice movement | admin | Correct date `2026-06-20` | 2 |
| Last rice movement | user | Correctly unable to determine | 2 |
| More than 90 days without movement | admin | HTTP 500 after inventory and movement reads; provider returned HTTP 413/token-rate failure | 2 successful reads |
| More than 90 days without movement | user | Correctly unable to determine; no movement primitive ran | 3 |

That admin composition attempt read 20 inventory rows and 40 movement
rows. It used 2 successful primitives, with no pagination continuation. Groq
returned HTTP 413 after those reads; the bounded transport behavior returned a
safe failure and did not replay the reads. The other admin water 500 was
accompanied by two generic EF Core command failure events, but the bounded
diagnostic capture does not identify a reliable cause. Earlier live rounds
did answer the water question for both identities and the rice question for
admin; those successes do not make the final six-case run pass.

The restricted identity never received the movements primitive and never
executed it. User answers on the history/stagnation questions disclosed no
permission, operation or endpoint details. That failed run therefore
confirms access filtering, but the full live acceptance matrix remains
unverified due to provider failure and the unexplained admin water error.

## Follow-up diagnosis and corrections

The water admin 500 is reproduced and isolated. The model was offered all five
WMS primitives plus the three existing catalogue capabilities. It attempted
`wms_items_list` with `sku: "AGUA X 1L"`. The WMS code validator raised
`Alxarafe.Modularity.ReadPrimitiveInputException` before any EF query. The router
correctly classified it as `InvalidRequest`; the assistant's immediate-failure
handling turned that into HTTP 500. This was not authorization, concurrency or
persistence failure. Direct invocation with those arguments reproduces the
validation error; listing references and using `AGUA-X-1L` returns 600 UN.

The assistant now returns a bounded validation result to the model so it can
correct its arguments. The same eight-call budget counts rejected attempts.
Schemas expose the existing code format, and instructions require resolving and
copying exact identifiers instead of constructing codes from descriptions.

The separate 413 diagnosis captured a provider `rate_limit_exceeded` response:
Limit 7000, Requested 7203 tokens. The request was 25,487 serialized bytes,
including 5,330 bytes of tool schemas and eight messages. That diagnostic
reproduction read two disjoint movement ranges and the reference catalogue.
The earlier inventory/movement reads serialized approximately 2,357 bytes for
20 balances and 8,342 bytes for 40 movements. This is the provider's token-rate
ceiling for a single request, not the model context window or an HTTP byte-size
limit. Stateless completions include schemas each time and prior results once;
there was no duplicate insertion of earlier tool messages within a request.

Groq transport now encodes uniform pages losslessly as columns/rows, storing
constant fields once. All values, duplicate records and pagination remain.
Redundant schema descriptions were shortened while retaining types, limits and
semantics. The full movement page shrank from 8,342 to 4,482 bytes in the model
message. A later request containing that page and reference results was 16,710
bytes (eight messages; 4,836 schema bytes), down from the 25,487-byte diagnostic
request. This establishes a transport improvement, not a completed answer.

After the first correction, water admin returned the correct 600 UN; a separate
water user diagnosis also returned 600 UN. An intervening six-case run found a
wrong empty-stock answer for user and another 413 before the final schema and
constant-column refinements. It was correctly rejected by the functional oracle.
The last isolated C run after those refinements ended in provider HTTP 429,
classified `daily_token_limit`, and public 503. Its final invocation had read
40 movements, an empty reference lookup, and 15 references; it did not reach a
final answer or the inventory read. No claim of C resolution is made.

The daily quota is now an external validation blocker. The final six-case matrix
has not passed on the current source. Following the explicit validation gate,
the new regression tests, architecture/integration suites, formatting and full
`bin/check` have not been rerun after these corrections, and there is no .NET
commit. Host compilation passed without warnings. Temporary parameter/exception
logging and the direct diagnostic test were removed; only bounded numeric audit
metadata and optional debug size metrics remain. Validation containers/databases
were cleaned up and log secrecy passed.

No specialized capability is justified by the 413 alone. Complete the live
composition measurement after quota is available before evaluating a separate
capability proposal. No capability or budget increase is part of this change.

## GPT-OSS model-only continuation

The six-case matrix was rerun on 2026-10-24 with the unchanged candidate code,
questions, tools, recovery mechanisms and eight-call/eight-turn budget. Only the
local test model changed to `openai/gpt-oss-20b`; production defaults and the
shared contract were not changed. The ignored local `.env` now selects that model.
The command was:

```sh
AI_GROQ_MODEL=openai/gpt-oss-20b python3 /tmp/ai-api/final-run.py --skip-candidate
```

This reused prior candidate-conformance evidence; it did not rerun candidate
conformance. The temporary runner and public results remain local artifacts,
not supported repository commands. The independent dataset again returned
`loaded`, then `already_loaded`, with stock and functional-oracle checks passing.

| Case | Identity | HTTP | Successful primitives, in order | Calls / model turns | Manual result |
| --- | --- | ---: | --- | --- | --- |
| Water | admin | 200 | items, inventory | 2 / 3 | Correct locations and balances; missing required explicit total 600 UN |
| Water | user | 200 | items, inventory | 2 / 3 | Correct locations and balances; missing required explicit total 600 UN |
| Last rice movement | admin | 200 | items, movements | 2 / 3 | Correct: 2026-06-20, ISSUE, -12 UN, B.01.02 |
| Last rice movement | user | 200 | none | 0 / 1 | Correct functional information limitation |
| More than 90 days | admin | 200 | items, movements | 2 / 3 | Exact six expected references |
| More than 90 days | user | 200 | none | 0 / 1 | Correct functional information limitation |

The stagnation answer lists `AGUA-X-15L`, `ARROZ-1KG`, `ATUN-3X80G`,
`AZUCAR-1KG`, `CONS-TOM-400G` and `DETERG-5L`, with no extra reference.
Restricted-user answers are the localized functional limitation, with no internal
permission, operation or endpoint details and no movement execution.

The runner exited **1**, reporting `Water oracle mismatch`: neither water answer
contains the required explicit total, although both correctly list A.01.01 = 480 UN
and B.01.04 = 120 UN. This is not a completed six-case acceptance pass. The rice
answer also uses Unicode nonbreaking hyphens in its correct date; its date fails
the runner's ASCII substring alternative despite being semantically correct.
The runner stops assertions at the first water mismatch; rice, stagnation and
restricted-user outcomes above are separate manual reviews of all six saved
responses, not claims that those later assertions executed.

No backend primitive attempt was InvalidRequest, Denied or Failed in this run.
Argument recovery remained enabled but was not exercised. Bounded audits record
paging metadata rather than filter values, so exact model-generated filters
cannot be compared with Qwen from these artifacts. All reads used offset 0 and
limit 100: water read 15 items then 2 balances; rice read 15 items then 2 movements;
stagnation read 15 items then 13 movements. No continuation was needed.

Stagnation admin encountered provider HTTP 400 and used the existing single
`tool_generation_retry`, then HTTP 429/token-rate limiting and the existing
`Retry-After` recovery. Both recoveries succeeded. That case used five HTTP
completion attempts across three model turns; successful reads were not replayed.
All other cases used one HTTP attempt per model turn. No 413 or daily-token-limit
failure occurred. Host audit recorded all six invocations completed. Log secrecy
and validation-container/database cleanup passed.

Compared with the previously recorded Qwen matrix, GPT-OSS used two successful
primitives for each authorized business question and zero for the restricted
history/stagnation questions. Qwen water user used four, rice admin/user two each,
and stagnation user three; the failed Qwen admin cases do not provide equivalent
successful-answer measurements. GPT-OSS resolved items before the relevant read;
no new capability was selected. Unlike the diagnosed Qwen `AGUA X 1L` invalid
argument, GPT-OSS produced no backend-rejected argument. Exact filter strings are
not audited. GPT-OSS returned all six public responses successfully, but omitted
the explicit water total that Qwen's successful water response included.

The acceptance gate remains unmet. Focused regression tests, architecture,
integration, formatting and full .NET `./bin/check` were therefore not started
in this continuation, following the requested matrix-first gate. No commit was
created, no prompt/oracle was relaxed and no implementation optimization was made.

## Closed provider observation phase

The provider phase is now closed by explicit task decision. Real-provider runs
are observational evidence, **not a blocking or deterministic acceptance criterion
for AI-API-001**. Earlier matrix-first gates and quota blockers above describe
historical decisions; their failures and unverified assertions remain unchanged.
No historical matrix is relabelled green. Groq is not a deterministic acceptance
source. Final acceptance uses the repository's deterministic validations below.

### Semantic water oracle and subsequent two-case run

Only the local water oracle was changed: it checks A.01.01 = 480 UN and
B.01.04 = 120 UN with no false locations/quantities. An explicit total is optional
for the location question; if present it must be 600 UN. Productive code, prompts,
model, budget, candidate contract and architecture were unchanged. The oracle
was checked against swapped balances, invented locations/quantities and an
incorrect total. This change does not retroactively turn the previous strict
runner's exit status into a pass.

The subsequent water-only run used the same questions and GPT-OSS model:

| Identity | HTTP | Observed final answer | Successful primitives | Semantic water result |
| --- | ---: | --- | --- | --- |
| admin | 200 | No puedo determinarlo con la información que ERBAS pone a mi disposición actualmente. | none | Failed: no location facts |
| user | 200 | A.01.01 = 480 units; B.01.04 = 120 units, no explicit total | items, inventory | Passed |

The runner exited 1 on admin; a separate evaluation of both saved responses
confirmed admin failure and user success. Both provider requests for this run
returned 200; user used three model turns, admin one. No backend read attempt
failed or was denied. Log secrecy and validation-resource cleanup passed. The
conditional complete matrix was not executed. The difference between the earlier
successful admin answer and this unresolved answer is observed variability in
outcome. The original run did not capture raw `finish_reason` or actual offered
tool names, so it does not establish a provider-level stop/no-tool diagnosis.

### Last single admin observation

Exactly one later admin water invocation was attempted with GPT-OSS. An external,
temporary observation proxy was used without changing backend source. The
invocation failed before an observable provider completion was captured. No
`finish_reason`, effectively offered tool count/names or returned tool calls
were recorded; the saved public final-answer value is null. These metadata are
**unknown**, not zero and not `stop`. This observation establishes neither
model-selection variability nor an authorization, routing or primitive failure.
No additional attempt was made to obtain a passing answer. Temporary containers
and validation databases were removed, and log secrecy passed.

### Preserved conclusions

- Successful GPT-OSS water admin/user runs returned the two correct balances;
  their omitted total caused the original strict textual-oracle failures.
- Rice admin returned the correct 2026-06-20 ISSUE of 12 UN; rice user returned
  only the functional limitation, with no primitive execution.
- Stagnation admin returned exactly the six expected references using **two
  successful primitives**, items and movements. Stagnation user returned only
  the functional limitation with zero primitives.
- The lossless transport optimization and measured 413 reduction are documented
  above; they do not guarantee external-model quality or quota availability.
- Tool-generation and rate-limit recoveries, invalid-argument recovery, quota
  failures, functional failures, changing admin outcomes and the final
  unobservable invocation are all retained as evidence, without retries to
  manufacture a green matrix.
- No further provider call is part of final validation. Provider credentials
  remain backend-local and untracked; no prompts, raw provider reasoning or
  credentials are included in this report.

### Earlier direct provider probes

Before the backend continuation, a direct GPT-OSS completion with `max_tokens: 8`
returned HTTP 200 but empty final content and `finish_reason: length`; it exhausted
the output allowance during reasoning. A separate direct request declaring only
`wms_inventory_list` returned HTTP 200, `finish_reason: tool_calls` and a structured
call with `sku: "Agua X"`. That request declared a tool but did not execute it;
it is provider tool-generation evidence, not ERBAS routing, inventory correctness
or backend-conformance evidence. No new direct provider probe was made during
final deterministic validation.

## Final deterministic acceptance

All following results were obtained after closing the observational provider
phase. Groq credentials were overridden with empty values for runtime checks;
Testing uses the deterministic adapter and transport tests use synthetic HTTP
responses. No external-provider call was made.

| Validation | Actual result |
| --- | --- |
| Docker application image build | Passed |
| `dotnet restore Alxarafe.sln` and `dotnet build Alxarafe.sln --no-restore` in Docker | Passed, zero warnings/errors |
| Focused Host integration tests: GroqAiModelClientTests, ReadPrimitiveTests, WmsDevelopmentAuthorizationTests | 36 passed, none failed/skipped |
| `dotnet test tests/Alxarafe.ArchitectureTests --no-build` in Docker | 130 passed, none failed/skipped |
| `dotnet test tests/Alxarafe.Host.IntegrationTests --no-build` in Docker | 316 passed, none failed/skipped |
| `dotnet format Alxarafe.sln --no-restore --verify-no-changes` in Docker | Passed after formatting-only corrections |
| `git diff --check` | Passed |
| `AI_GROQ_API_KEY= AI__Groq__ApiKey= ERBAS_CONTRACT_DIR=/tmp/ai-api/contract-v0.11.1 ./bin/check` | Passed; published v0.11.1 baseline |
| Candidate `/home/rsanjose/Desarrollo/Alxarafe/erbas-contract/bin/test --network NETWORK http://validation-app:8080` against a separate fresh deterministic Host | Passed; candidate checkout `fb43ce6` |

Focused tests ran with `--filter
'FullyQualifiedName~GroqAiModelClientTests|FullyQualifiedName~ReadPrimitiveTests|FullyQualifiedName~WmsDevelopmentAuthorizationTests'`
and `--no-build`. Runtime tests used backend-owned disposable Platform/WMS and
Security databases on the Compose network. `./bin/check` rebuilt the image and
solution and repeated all 130 architecture and 316 integration tests after the
formatting corrections. The full check also passed the platform/module Bruno
suites, fresh-install migrations, persisted-data invariants, log secrecy and
database/container cleanup. Candidate conformance separately passed log secrecy
and cleanup; it does not change `contract.revision` or publish/adopt v0.12.0.

The first whole-solution format verification failed on whitespace/import ordering.
Only the reported formatting was corrected, including pre-existing issues in
CollectionPagination, LoginEndpoint, PermissionsAuthorization and
CapabilityInvocationApiTests. No behavior, prompt, budget, model, contract or
architecture was changed in this closure. The corrected whole-solution format
verification and final full check passed.

Security coverage includes persisted effective admin/user discovery, omitted
unauthorized movement tools, forged-claim rejection, module availability,
execution-time reauthorization, permission revocation after discovery and between
discovery/execution, closed typed input validation, real inventory/movement
reads, unique ordered provenance, localized functional limitations, private
failure provenance, invalid-argument correction and the unchanged eight-attempt
budget. Transport tests verify bounded retries and lossless tabular encoding,
including constant fields, duplicate rows and pagination.

Final scope/documentation review retained the unpublished candidate boundary and
last published pin. The closure introduces no new behavioral literals. The
secret scan checked repository files against configured local credentials and
credential patterns; `.env` remains ignored/untracked. No temporary parameter,
exception, payload or raw-provider logging hook was found in source. The
external observation programs were removed; observational results are preserved
in this report. Existing bounded operational audit and numeric size metrics do
not log prompts, arguments, outputs, reasoning or credentials.

The contract repository remained clean at `fb43ce6` throughout this closure.
No push, MR/PR, merge, tag, release or publication is part of this task.

## Published v0.12.0 contractual adoption

The final adoption uses the normal immutable-release mechanism, after the
contract was merged and published. Remote references were fetched without
rebasing, merging or dropping the existing .NET commits `24825db` and `dd579ba`.
`contract.revision`, the release checks in `bin/check` and the CI checkout now
select `v0.12.0`. Current usage/design documentation was updated; earlier
verification results and provider observations above retain their historical
versions, failures and limitations.

The remote tag identity was checked with:

```sh
git ls-remote origin refs/tags/v0.12.0 'refs/tags/v0.12.0^{}'
```

- Published annotated tag object: `070990bef7bc0aa8ca8876c6b112d4edc6cf99d7`.
- Target commit: `0d3d1c1cd81cc39b9d9eb8840381c415cb909363`.
- Supplied checkout: `/home/rsanjose/Desarrollo/Alxarafe/erbas-contract`.
- Checkout HEAD and `origin/main` matched that target, with no tracked/nonignored
  changes. This is the normal contract repository, not a temporary candidate
  copy. Its contents were not modified during adoption.

| Adoption validation | Result |
| --- | --- |
| Published checkout `bin/test --network NETWORK http://validation-app:8080` against a fresh backend-owned deterministic Host/database | Passed |
| `docker compose run --rm --no-deps app dotnet format Alxarafe.sln --no-restore --verify-no-changes` | Passed, no formatting changes needed |
| `git diff --check` | Passed |
| `AI_GROQ_API_KEY= AI__Groq__ApiKey= ERBAS_CONTRACT_DIR=/home/rsanjose/Desarrollo/Alxarafe/erbas-contract ./bin/check` | Passed |

The full check validated the new immutable identities before invoking the
published runner. Docker image build, restore and build passed without warnings
or errors; all 130 architecture and 316 integration tests passed. These include
the affected AI/primitive/security/transport regressions; no separate focused
rerun was needed for a pin/CI/documentation-only adoption. Published shared
conformance, local platform/module Bruno, persistence invariants, secret-log
checks and validation-resource cleanup all passed. The independent conformance
run also passed log secrecy and cleanup.

No functional AI source, tests, prompt, model, eight-call/eight-turn budget or
architecture changed. No provider call was made; Groq credentials were overridden
with empty runtime values. Real-provider evidence remains observational and is
not deterministic acceptance. Scope and documentation review found only the
release pin, immutable identities, matching CI checkout and adoption documentation
in the final diff. This task creates one local adoption commit only; it does not
push, open an MR/PR, merge, tag, release or publish anything.
