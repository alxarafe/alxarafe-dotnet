# AI-001 .NET implementation verification

Date: 2026-10-22. Historical scope: published ERBAS Contract `v0.11.0`, AI-001 in
the .NET backend. Current adoption: published immutable `v0.11.1` runner PATCH. All
commands ran in Docker against disposable PostgreSQL databases. No host .NET
runtime, live provider, provider key or internet model call was used.

## Historical v0.11.0 validation

| Command | Result |
| --- | --- |
| `docker compose run --rm --no-deps app sh -c 'dotnet build Alxarafe.sln'` | PASS: 0 warnings, 0 errors. |
| `./bin/test` with the disposable validation administrator | PASS: 118 architecture tests and 258 integration tests. |
| `ERBAS_CONTRACT_DIR=/.../erbas-contract-v0.11.0 ./bin/check` | PASS: build, native tests, clean migrations, isolated Host, shared conformance, local Bruno, persistence and secrecy checks, cleanup. |
| Focused `AiAssistantApiTests` and `ModuleCompositionTests` | PASS: 14/14. |

The pinned checkout was tag object
`fe25924bcf92b30977be3ef3ffb2d484445e0e98`, resolving to commit
`1211a48b78452db794c5ffb01ff2e1d0ba56605a`, clean at HEAD. Shared conformance
executed the sole collection (590 requests / 2587 named checks) against the
isolated validation Host.

## Current v0.11.1 validation

| Command | Result |
| --- | --- |
| `git diff --check` | PASS: no whitespace errors. |
| `ERBAS_CONTRACT_DIR=/.../erbas-contract-v0.11.1 ./bin/check` | PASS: build, 118 architecture tests, 258 integration tests, clean migrations, isolated Host, shared conformance, local Bruno, persistence and secrecy checks, cleanup. |

The pinned checkout is tag object
`5ea047b3375cf0362397ee1e21c95d4991f09c10`, resolving to commit
`5517f0df5d3cacc57c35ef704131dcb30ad9dde2`, clean at HEAD. The build reported
0 warnings and 0 errors. Shared conformance passed under the v0.11.1 180-second
full-conformance watchdog; the contract runner container completed in 116 seconds.
The complete `./bin/check` completed in 243 seconds. The collection remains 590
requests / 2587 named checks, and AI-001 runtime behavior is unchanged.

## Coverage

Native tests cover authentication -> AI permission -> module gate -> body
validation precedence, closed message validation including the 4000-scalar limit,
NUL and unpaired surrogates, direct answers, one and multiple ordered CORE tool
invocations, distinct generated identities, AI-only authorization without
business visibility, disabled/restored WMS tools, disabled/restored AI module,
temporary and permanent model failures, unknown tool failure, execution budget
exhaustion, per-tool re-evaluation, and AI/CORE audit linkage without payload
logging. Composition tests cover the normal `erbas.ai` + `erbas.wms` registry and
the two explicit conformance descriptors.

Shared AI scenarios in the pinned collection additionally verify the public
request/response shapes, masked errors, no-store headers, Bearer challenge rules,
AI-only authorization and deterministic success behavior.

## Deliberate limits

- Testing uses `DeterministicAiModelClient`; CI and shared conformance do not call
  Groq or any external model. Live provider quality, latency, rate limiting and
  timeout behavior were not exercised.
- Audit linkage is verified through captured structured logs; external audit
  persistence is not implemented.
- Conversations, history, memory, RAG, WRITE execution, streaming, approvals,
  model selection, provider fallback and public provider metadata remain out of
  scope.
- The contract checkout is read-only; this report does not modify, retag or
  republish it, and does not claim Java or client v0.11.0 or v0.11.1 adoption.
