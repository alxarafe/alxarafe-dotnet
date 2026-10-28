# RESOURCE-PRESENTATION-001: .NET contextual presentation

## Contract identity and status

The backend formally adopts **published immutable erbas-contract v0.14.0** in
[`contract.revision`](../contract.revision), normal `bin/check` and CI.

- Annotated tag object: `22191afb8ab477fef319093b8c212264f4019816`.
- Target commit: `9919ad3725e50d63e7a19befbe0d81b4d91df0cc`.

The normative document is
[RESOURCE-PRESENTATION-001 in v0.14.0](https://github.com/alxarafe/erbas-contract/blob/v0.14.0/docs/core/resource-presentation-001.md).
Normal conformance verifies this clean published tag directly. No second candidate
run is required. The implementation was prepared against the same commit before
publication; that historical development evidence is retained below.
Java compatibility/adoption is not claimed. `erbas-client` implemented/tested the
capability but still formally pins v0.13.0; this backend adoption does not update it.

## Model and HTTP boundary

`ResourceDefinition.ContextualPresentation` is optional immutable metadata in
`Alxarafe.Resources`. It holds optional `List`, `Create`, `Edit` and `View`.
LIST declares ordered field columns with optional plain labels and action names.
CREATE/EDIT/VIEW reuse `ResourceFormDefinition`: behavior, ordered tabs, sections,
groups, field references and action placements. There is no second layout
language, frontend type, renderer identifier, CSS or executable metadata.

The frozen registry validates known, unique field/action references in each
context, including across containers. Tab IDs are unique within a form, section
IDs within a tab, group IDs within a section. Contexts may reuse IDs independently.
CREATE accepts COLLECTION actions; EDIT/VIEW ENTITY; LIST COLLECTION/SELECTION.
Existing label, behavior and collapse rules apply. Contexts may deliberately be
empty or omit fields. Legacy form completeness/exclusion validation is unchanged.

`GET /api/meta/resources/{resource}/presentation` returns the closed
`{id, definitionHash, presentation}` shape. The last object contains only declared
contexts; an existing legacy resource returns `presentation: {}` with HTTP 200.
No properties are added to the published `ResourceMetadata`/`presentation.form`
wire shape. Existing CRUD routes, generated CRUD operations and persistence are
unchanged. The host OpenAPI additionally describes the new endpoint and its DTOs.

Bearer authentication precedes query validation and lookup. Queries return
`400 invalid_request`; unknown resources `404 resource_not_found`; anonymous
requests `401 unauthorized` with a Bearer challenge. Static discovery needs no
CRUD permission or active tenant. Success and conditional 304 responses carry
`Cache-Control: private, no-cache`, `Vary: Authorization` and a quoted ETag.
The current definition is resolved before If-None-Match; matching strong/weak or
wildcard conditions produce 304 without a body. Errors use the existing closed
shared error handling and no-store.

## Identity, fallback and authority

The definition hash uses the existing single SHA-256 canonicalization pipeline.
Legacy definitions retain canonical format v3 and its golden vector. Contextual
definitions select v4 and include all ordered declarations, including the
difference between absent and explicitly empty contexts. A contextual change
changes the detailed metadata hash and its ETag as well. The presentation endpoint
uses that same definition hash as its static ETag; there is no independent layout
hash. Clients must match resource ID and definitionHash with detailed metadata
before applying a presentation document.

The server projects declarations, not a resolved or merged layout. The consumer
resolution order remains:

| Context | Declared | If absent | If also absent |
| --- | --- | --- | --- |
| CREATE / EDIT / VIEW | Exact contextual form | Legacy `presentation.form` | Metadata field order |
| LIST | Exact contextual columns | Metadata field order | — |

Present empty forms/columns are intentional. Omitted fields are not appended to an
explicit context. Legacy fallback preserves placement and appends eligible
unplaced fields in metadata order according to the contract. Static `UI.Hidden`
and `UI.FormExcluded` hints remain in field metadata: consumers apply them over
placement (Hidden in every context; FormExcluded in forms, not LIST). Contexts
cannot override them or change types, constraints, defaults, required/nullable,
relations, immutability or permissions.

Presentation is not authorization. Effective CRUD/action capabilities and all
backend execution guards remain authoritative. A user without UPDATE may receive
the same static EDIT/VIEW document. VIEW is read-only presentation, not revocation
of authorized entity actions. Actions are existing scoped operations, not CREATE
hooks; placements neither authorize nor execute them. Runtime does not interpret
contextual declarations.

## Assets: generic two-phase presentation

The Assets module declares contexts for `assets.asset`; framework projects have
no resource-name exception. Other resources continue returning `{}` when they
have no declarations.

- CREATE places **assetTypeId, name**, in that order, using the existing form
  hierarchy. Ordinary POST creates the existing minimal DRAFT Asset.
- EDIT reuses the complete existing Asset form (General, Purchase and notes,
  Location and custody, Audit), including existing entity action placements.
- VIEW reuses the same complete form; consumers render fields read-only.
- LIST columns are **name, assetTypeId, assetTag, lifecycle, condition, locationId**,
  in that order. Name identifies the record, type classifies it, tag supplies a
  business identifier, lifecycle/condition show its state and location supplies
  its base relation. Every column is an existing field; no synthetic display field,
  join or persistence change is added.

`assetTypeId` remains immutable: accepting it at creation does not make it PATCH
writable. Creation defaults, lifecycle, dynamic fields, custody, tenant isolation
and persistence rules remain unchanged. HTTP/PostgreSQL tests exercise minimal
POST → DRAFT → full-context PATCH and reject a type change using existing guards.
An architecture test detects Assets-specific names/references in framework source;
Runtime tests enforce that execution does not depend on contextual presentation.

## Verification

Use [normal full published validation](usage.md#full-validation) against v0.14.0.
Optional candidate verification is reserved for future development. Current
[published-adoption results](generic-resource-contract.md#published-v0140-adoption-verification)
record the single v0.14.0 conformance run and unchanged 1,139-test suite.
Focused coverage includes all contexts/order, explicit empty versus absent,
legacy coexistence, labels, static hints, reference/container/scope rejection,
canonical golden vectors and mutations, closed DTOs, authentication/query/lookup
precedence, tenant/permission-independent discovery, definition binding,
ETag/304 and actual Assets creation/editing/persistence. No new dependency,
migration, lifecycle rule, field permission or frontend implementation is added.

### Historical pre-publication verification

The following evidence records the original implementation before publication and
formal v0.14.0 adoption. Its published release was v0.13.0 (tag object
`919684db4f607a575397576b87577cd9582e1f86`, target
`29bd8de3e5c5772fea5096ab2c60abc6a10e55f7`); its candidate was the exact commit now
published as v0.14.0. The original two runs do not describe the current normal path.

Local authoritative verification completed on 2026-10-28:

- Normal `ERBAS_CONTRACT_DIR=<clean-v0.13.0> ./bin/check` passed against the exact
  published tag object/commit above.
- The additional candidate command passed with the exact clean candidate SHA
  above, after published conformance and fresh validation Host/databases.
- Both full runs passed **1,139 native tests**: 177 architecture, 668 Host
  integration, 181 metadata and 113 Runtime; zero failures or skips, zero build
  warnings/errors. This adds 45 cases to the prior 1,094-test baseline.
- Published and candidate shared conformance, host OpenAPI, local Bruno,
  PostgreSQL migration/storage ownership and persistence, sensitive-log checks
  and validation Host/four-database cleanup passed. These are real backend checks,
  not synthetic contract-runner fixture claims.
- Ten focused checkout-guard cases checked accepted published/candidate inputs
  and rejection of incomplete variables, non-SHA/wrong HEAD, dirty/wrong-origin/
  non-root candidates and a mismatched published tag object before Docker.
- Scoped Docker `dotnet format`, shell syntax, local documentation links, final
  diff and credential-marker/NUL review passed. The published pin, dependencies,
  migrations, Assets rules and all other repositories remain unchanged.
