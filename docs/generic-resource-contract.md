# Published Generic Resource Runtime contract adoption

The backend formally adopts published immutable erbas-contract **v0.13.0** through
`contract.revision`, `bin/check` and the matching CI checkout. This release contains
CRUD-CONTRACT-001 Generic Resource Runtime and NAVIGATION-CONTRACT-001 effective
application navigation. The normative protocol and cross-stack review are
`docs/core/resources-001.md`, `docs/core/navigation-001.md` and
`docs/core/resources-portability.md` in the explicitly supplied contract checkout.

- Annotated tag object: `919684db4f607a575397576b87577cd9582e1f86`.
- Target commit: `29bd8de3e5c5772fea5096ab2c60abc6a10e55f7`.

This adoption changes only the published pin, validation selection and related
status documentation. It does not change Assets, Runtime, navigation, permissions,
persistence, generated OpenAPI or HTTP behavior. It does not imply Java or client
adoption, or publication of a backend tag, GitHub Release or package.

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

Authoritative validation requires a clean checkout of the exact published release:

```bash
ERBAS_CONTRACT_DIR=/path/to/clean/v0.13.0 ./bin/check
```

The check verifies repository root, expected GitHub origin, exact annotated tag
object, target commit, matching HEAD, cleanliness and runner entrypoint. It invokes
that checkout's sole shared conformance collection directly on the isolated
validation network. The former v0.13.0 candidate override has been removed; there
is no second candidate path. CI checks out the same published tag.

Native tests, shared generic CRUD/navigation conformance, local Bruno, persistence,
log secrecy, architecture and disposable database/container cleanup remain
mandatory. Runner synthetic fixtures establish runner behavior only; native and
live backend evidence remain distinct.

## Client scope and limits

Static resource discovery, list/filter/sort/search/page, create/PATCH/delete,
MANY_TO_ONE reference lookups, neutral optional form layouts, action dialogs,
availability and common errors can use the protocol without Assets-specific
screens. Exact numeric handling is required. Effective capabilities do not imply
that a particular business state/context permits mutation. Dynamic Assets binding,
joins/expansion, OR/NOT, projection, cursors, workflows, bulk CRUD and stale-write
protection remain outside this protocol. Action availability remains advisory;
no business-specific availability providers are added here.

## Published adoption verification

Authoritative `ERBAS_CONTRACT_DIR=<clean-published-v0.13.0-checkout> ./bin/check`
passed after verifying the exact tag object, target commit and clean HEAD above:

- 1,094 native tests: 176 architecture, 655 Host integration, 150 metadata and
  113 Runtime; zero failures or skips, including existing Assets, generic CRUD
  and effective navigation coverage.
- The published tag's sole shared conformance collection passed directly against
  the isolated Host. OpenAPI validation and local platform Bruno also passed.
- Fresh migration/catalogue ownership, post-conformance persistence, sensitive-log
  checks and disposable validation Host/database cleanup passed.
- Focused checkout-guard checks rejected missing/moved tags, a mismatched tag
  object, dirty state, mismatched HEAD and unexpected origin before Docker.
  A valid published checkout was accepted; the removed candidate variable could
  no longer select an alternative checkout.

Historical candidate results below are separate evidence and were not rewritten
as published-release adoption results.

## Historical preparation verification

The following evidence predates formal published v0.13.0 adoption. References to
v0.12.0, local candidate commit `a44d441` and candidate validation retain their
historical meaning; they do not describe the current pin or supported check path.

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
