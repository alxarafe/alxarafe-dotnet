# Asset register queries and dynamic-property lookup (ASSET-005)

All operations use the existing generic Resource API and Resource Runtime.
Assets supplies PostgreSQL query translation; no resource-specific list route,
Core/Runtime extension, migration, physical column or new index is introduced.
This implementation-specific API is not promoted to the shared ERBAS contract.

## Ownership of semantics

Runtime owns `ResourceQuery`, field capability/type/constraint validation,
authorization, tenant/module guards and page validation. Assets owns EF/SQL
translation, search matching, default/final ordering and total calculation.
Dynamic filters are **B: module persistence responsibility**; relation navigation,
Boolean query trees, projection and cursor/snapshot protocols are **C: outside
scope**. No demonstrated **A: Runtime deficiency** requires a platform change.

Generic metadata exposes `filterable`, `sortable`, `searchable` and canonical
field types. Operator eligibility follows the existing Runtime type rules; there
is no new per-field operator-list property or Assets-specific client hint:

| Filterable type | Operators |
| --- | --- |
| STRING/TEXT | `eq`, `ne`, `lt`, `lte`, `gt`, `gte`, `contains`, `startsWith` |
| Numeric/DATE/DATETIME | `eq`, `ne`, `lt`, `lte`, `gt`, `gte` |
| BOOLEAN/UUID/ENUM | `eq`, `ne` |

Only nullable fields accept a JSON null filter value, and only with `eq`/`ne`.
The ordinary Runtime scalar/constraint checks also apply to filter operands.
Unknown fields, unsupported operators, incompatible values, invalid dates,
duplicate/invalid sorts and invalid pagination return 400. Existing permissions
still govern reads (403), unknown resources return 404, and no matches return
an ordinary empty page. Reads do not introduce business-conflict responses.
Write-side 409 behavior and ASSET-002/003/004 rules remain unchanged.

## Asset register fields

| Asset field | Filter | Sort | Search |
| --- | --- | --- | --- |
| `id` | Yes | Yes | No |
| `name`, `assetTag` | Yes | Yes | Yes |
| `assetTypeId`, `manufacturerId`, `assetModelId`, `locationId`, `statusId` | Yes | No | No |
| `lifecycle` | Yes | No | No |
| `condition`, `purchasePrice`, `purchaseDate` | Yes | Yes | No |
| `createdAt`, `updatedAt` | Yes | Yes | No |

The shared Assets definition builder also exposes existing created/updated
timestamps for filtering/sorting on its catalogue and dynamic resources.
Other fields remain non-queryable unless their metadata declares otherwise.
No `location.name`, related-child predicates, expansions or generic joins are
accepted. Manufacturer/model filters compare local IDs, with no relation traversal.

List `GET /api/resources/assets.asset` with repeated `filter` parameters.
Each filter is URL-encoded JSON with exactly `field`, `operator`, `value`:

```json
{"field":"assetTypeId","operator":"eq","value":"<type-uuid>"}
{"field":"condition","operator":"gte","value":50}
{"field":"purchaseDate","operator":"lt","value":"2025-01-01"}
```

Filters combine with AND before ordering, count and paging. Static dates map to
PostgreSQL dates; timestamps are canonical instants normalized to UTC. Static
purchase-price comparisons use the existing exact decimal column. Equality on
text is case-sensitive with the module's existing ordinal `C` collation.
`contains`/`startsWith` match literal text, not a caller-supplied SQL pattern.

Null semantics are explicit: `eq null` selects absent values and `ne null`
selects present values. Existing EF null compensation means `ne <non-null>` also
includes absent nullable values; ordered comparisons exclude them. Add
`ne null` when the query should compare only present values.

## Ordering, pagination and totals

Repeated `sort` names preserve caller precedence; prefix `-` for descending.
For example `sort=name&sort=-condition` sorts by name ascending, then condition
descending. The module appends ID ascending only if ID is not already requested;
an explicit ID sort retains its requested position/direction. Without sorts,
ID ascending is the default. UUID ordering is PostgreSQL native ordering, and
nullable sort columns use PostgreSQL defaults: NULLS LAST ascending, NULLS FIRST
descending. No automatic null-ordering grammar is added.

`offset` defaults to 0; `limit` defaults to 50 and must be 1..100. Ordering applies
before offset/limit. Assets always returns the exact matching `total` before
paging, including empty, beyond-end and final partial pages. The generic
Runtime page protocol itself still permits an absent total and has no `order`
member; ASSET-005 does not alter it or the shared collection contract.

Tests use tied primary and secondary keys, successive nonzero-offset pages,
repeated windows and a final partial page. Stability is guaranteed for unchanged
data with the same query. Count and items are separate SQL reads: no snapshot
consistency across concurrent writes or across page requests is promised.

## Search

Only Asset opts into generic `search`, using `name` and `assetTag`.
It is a case-sensitive literal substring match against either field, combined
with the other filters before count/paging. NULL tags do not match. Search is
not trimmed or tokenized; whitespace-only input is rejected by Runtime.
The existing maximum length is 512 characters. NUL is rejected with 400 at
the module persistence boundary because PostgreSQL text/JSONB cannot store it. `%`, `_`, apostrophes and Unicode
are literal input, not SQL wildcards or executable SQL. There is no stemming,
accent folding, fuzzy matching or full-text infrastructure.

## Dynamic typed lookup

Query `GET /api/resources/assets.asset-field-value`, combining ownership filters
such as `fieldDefinitionId` or `assetId` with a typed slot predicate. For example:

```json
{"field":"fieldDefinitionId","operator":"eq","value":"<definition-uuid>"}
{"field":"decimalValue","operator":"gt","value":12345678901234567890.123456789012345677}
```

The five existing slots are filterable, not sortable or searchable:
`stringValue`, `int32Value`, `decimalValue`, `booleanValue`, `dateValue`.
Their canonical types determine operators from the table above. Slot predicates
always restrict the owning definition's type, including `ne`: INT32 never
matches a DECIMAL row with the same JSON number. Different slot predicates on
one row therefore have no matches. No implicit OR is added. Repeated predicates
on the same slot compose with distinct bound parameters.

STRING query operands containing NUL are likewise rejected with 400.
Slots are non-nullable. An absent slot is not JSON null or a stored value, and
`slot eq null` is rejected. A missing field value is the absence of a resource
row; finding Assets with no such row requires a future separate query design.
Inactive definitions' persisted values remain queryable history; write guards
are unchanged. Definition-specific constraints govern writes, while query
operands use the slot's canonical scalar domain, not the selected definition's
business range.

The repository uses its existing definition navigation for type qualification
and parameterized SQL subqueries over the single JSONB value column. The public
Runtime projection serializes every operand. PostgreSQL JSONB numeric comparison
uses exact numeric semantics, preserving values beyond CLR decimal/double.
BOOLEAN compares as JSON booleans. STRING uses root scalar text extraction with
`C` collation and literal `strpos`/`starts_with` for fragments. DATE uses the
same canonical fixed-width `yyyy-MM-dd` representation produced/parsed by
Runtime; ordinal comparison equals calendar order over that domain and avoids
session timezone/DateStyle conversion. No second date or number format is added.
SQL structure comes from module constants and validated operators; all caller
values are bound parameters. No in-memory filtering precedes pagination.

This is value-resource lookup, not an Asset-to-values join or a special Asset
search endpoint. Consumers receive the matching values and their `assetId`.
The tenant database resolver scopes every query, including its subqueries.

## Storage and remaining limits

Existing definition/ownership lookup indexes remain in use as available to the
PostgreSQL planner. No content index is added: there is no measured workload or
query-plan evidence justifying a GIN/partial/expression index yet. Such tuning
requires representative data and well-defined typed predicates. No performance
or index-selection guarantee is claimed. Physical tables/columns and migrations
remain unchanged; EF reports no pending schema changes.

Unsupported features remain relation paths/joins, inverse navigation,
cross-property Asset queries, OR/NOT trees, select/projection, dynamic-slot
ordering/search, cursor pagination, full-text/fuzzy search, and consistent
snapshots of a changing register. Generic query discovery still uses field
capabilities plus known Runtime type rules; promotion to a portable shared
protocol is a separate approved task.

## Verification

Validation completed successfully:

- Focused Docker verification passed 109 HTTP/PostgreSQL/integration cases and
  five Host boundary checks, with zero skips. It includes canonical dynamic
  comparison matrices and a direct ResourceRuntime exact-decimal query.
- Final authoritative `./bin/check`, using `ERBAS_CONTRACT_DIR` for the clean
  pinned v0.12.0 validation checkout, passed 1,062 native tests: 176 architecture,
  633 Host integration (29 new query cases), 150 metadata and 103 Runtime.
  No skips, build warnings or build errors. Existing ASSET-002/003/004 tests pass.
- Docker image/Release publish, isolated Host startup, shared OpenAPI/Bruno
  conformance, local Bruno suites, PostgreSQL persistence checks, log secrecy
  and disposable database/container cleanup all passed.
- Assertions cover AND filtering before totals/windows, all six numeric/date
  comparisons, timestamp offset equivalence, nullable comparisons and sorting,
  tied multi-column sorting, explicit ID precedence, repeated offset windows,
  partial/beyond-end pages, literal search, invalid operator/type requests,
  canonical dynamic types, exact 38-digit decimal boundaries, repeated bound
  parameters, inactive history, tenant isolation and metadata capabilities.
  Final validation also covers NUL rejection with 400 on new search/dynamic
  string paths. No physical schema changes are pending.
- The initial SQL build triggered EF1002; the final implementation uses bound
  values through `FromSqlInterpolated`, with no analyzer suppression. The first
  full run passed; full validation was repeated after adding the NUL boundary
  checks. SQL-like strings and wildcard characters remain literal query data.
- Docker whitespace formatting, final diff/scope review, UTF-8/NUL/credential
  marker review and documentation link checks passed. Only Assets code, query
  integration tests and documentation changed. No Core/Runtime, shared contract,
  shared Bruno, migration, dependency/lockfile or frontend changes were made.
