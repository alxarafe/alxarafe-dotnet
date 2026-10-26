# Generic Resource Runtime contract candidate

CRUD-CONTRACT-001 aligns generic HTTP descriptions with the approved v0.13.0
local candidate in erbas-contract (local commit `a44d441`). The published backend pin remains v0.12.0;
no tag, release or adoption is implied. The normative candidate protocol and
cross-stack review are `docs/core/resources-001.md` and
`docs/core/resources-portability.md` in the explicitly supplied contract checkout.

The transport and Runtime guard order are unchanged. The protocol has an
approved exception strictly limited to generic CRUD, metadata and action routes;
legacy Users/Modules/WMS conventions are unaffected. Native tests assert both
the metadata/operator contract and observable authentication/lookup/transport/
permission precedence. Existing module/tenant-before-permission tests remain.

ResourcePage remains items/offset/limit with optional total, without order.
It is deliberately separate from legacy shared collections. PATCH remains a
partial field object, with omitted/null/default/immutable distinctions. Query
operators come from a single typed Runtime catalogue reused by validation and
metadata projection. Capabilities now explicitly include filterOperators.
Generated CRUD OpenAPI includes 409. No Assets code, business semantics,
persistence, record concurrency or client code is changed.

## Validation

Normal authoritative validation still requires the exact published release:

```bash
ERBAS_CONTRACT_DIR=/path/to/clean/v0.12.0 ./bin/check
```

Additionally, explicit development-only candidate validation uses a separate
fresh disposable environment, while still verifying the published pin:

```bash
ERBAS_CONTRACT_DIR=/path/to/clean/v0.12.0 \
ERBAS_CONTRACT_CANDIDATE_DIR=/path/to/explicit/v0.13.0-candidate ./bin/check
```

The opt-in checks repository root/origin, candidate version and runner entrypoint,
records candidate HEAD and working status, and invokes that checkout's sole
shared conformance collection. It does not edit contract.revision or CI adoption.
Candidate full conformance runs on a new database because earlier shared suites
intentionally mutate their catalogues. Runner synthetic fixtures establish runner
behavior only; native and live backend evidence remain distinct.

## Client scope and limits

Static resource discovery, list/filter/sort/search/page, create/PATCH/delete,
MANY_TO_ONE reference lookups, neutral optional form layouts, action dialogs,
availability and common errors can use the protocol without Assets-specific
screens. Exact numeric handling is required. Effective capabilities do not imply
that a particular business state/context permits mutation. Dynamic Assets binding,
joins/expansion, OR/NOT, projection, cursors, workflows, bulk CRUD and stale-write
protection remain outside this candidate. Action availability remains advisory;
no business-specific availability providers are added here.

Verification completed:

- Focused Docker verification passed 83 HTTP/PostgreSQL integration cases and
  five Host boundary checks before the final page/path regression additions.
- Authoritative published-pin check passed against immutable v0.12.0 with
  1,074 native tests and legacy shared conformance.
- Explicit candidate full check passed; after final review restored exact query
  issue paths and added a regression, it passed again with 1,076 native tests:
  176 architecture, 637 Host integration, 150 metadata and 113 Runtime; no skips,
  warnings or errors. All existing Assets cases remain passing.
- Candidate conformance uses the same single shared collection (611 physical
  requests / 2,639 named checks). Metadata-driven iteration exercised production
  resources without Assets-specific test branches. Shared and local Bruno,
  Release image/publish, isolated startup, persistence, log secrecy and disposable
  database/container cleanup passed.
- Contract repository authoritative verification passed 213 tests, including
  preservation of published definitions and negative runner mutations. Synthetic
  fixture evidence is distinct from the real .NET conformance run.
- Docker whitespace formatting, final diff/scope review, credential-marker/NUL
  review and documentation links passed. No Assets, guard-order, tenant-resolution,
  dependency/lockfile, shared-error-shape or client changes were introduced.

The only query-validation refactor reuses the existing typed operator policy;
unknown-field/operator paths and incompatible typed-operator paths are preserved
explicitly by regression coverage. Parsing uses the same token names and stage.
The published pin and CI checkout remain v0.12.0. Candidate validation is local
development evidence, not publication or adoption.
