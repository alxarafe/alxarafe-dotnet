# Assets demonstration data

## Purpose and opt-in

ASSETS-DEMO-DATA-001 provides fictional, deterministic equipment records for
showing the existing generic CRUD and, later, verifying factual READ/AI answers.
It adds no AI behavior, public endpoint, resource definition or schema migration.
Use only in disposable development/demo tenant databases, never real operational
or production data.

`AssetsSeed` remains the structural bootstrap: LAPTOP, DELL, Latitude 5450,
Main office and the existing reserved statuses. It runs independently of this
feature. `AssetsDemoSeed` runs separately after migrations and structural seed,
only when `Assets:DemoData:Enabled=true`. Missing or false means no demo rows.
Development alone does not enable it.

The development Compose service maps `ERBAS_ASSETS_DEMO_DATA` (default `false`)
to `Assets__DemoData__Enabled`. Set a local administrator password using the
existing `.env`/environment mechanism, then start with:

```bash
ERBAS_ASSETS_DEMO_DATA=true ./bin/up
```

`.env.example` documents the default. The validation Host has no demo mapping;
`bin/test` explicitly disables the inherited development flag. Dedicated seed
integration tests opt in against their own fresh PostgreSQL databases. Testing
or Production never opt in automatically. Explicit configuration applies through
the existing tenant migration pipeline to each registered tenant database; do not
enable it on a shared/operational installation.

## Ownership, repeatability and conflicts

The exact canonical IDs use namespace `a55e7d00-0000-4000-8000-` and a twelve-digit
numeric suffix. Only named IDs are owned, not every row with that prefix:

| Suffixes | Owned records |
| --- | --- |
| 101–104 | MONITOR, PHONE, PRINTER, SERVER |
| 201–204 | LENOVO, HP, SAMSUNG, APPLE |
| 301–312 | Additional models, in the order below |
| 401–403 | Warehouse, Technical lab, Remote office |
| 501–503 | RAM_GB, ENCRYPTED, WARRANTY_UNTIL |
| 1001–1024 | The 24 equipment records, in table order |
| 2000 + equipment number × 10 + field position | Present dynamic values (positions 1–3) |
| 3001–3012 | Eight current and four closed assignment rows, in the order below |

Bootstrap identities are reused, not adopted into demo ownership. User-created
IDs outside these exact sets are never deleted or updated. Natural-key collisions
(e.g. an existing user-owned LENOVO code or asset tag) cause atomic rollback,
not lookup-by-code adoption. Canonical rows are normalized on startup, including
fixed values/timestamps; edits to them are therefore intentionally reset. Use
separate user-created records when edits should survive reseeding.

The module's existing mutation transaction/advisory lock and domain validators
protect each tenant load. Only the twelve canonical assignment IDs are replaced;
foreign history is preserved. Conflicting user custody or incompatible existing
completed LAPTOPs cause rollback rather than removal or modification. Adding the
two required LAPTOP definitions must satisfy existing definition-evolution rules;
a database with completed user LAPTOPs lacking those values is not a suitable
empty demo database. No rules were relaxed to accommodate demo data.

Seed-owned catalogue/asset/definition/value timestamps are fixed at
2024-01-01 (created) and 2026-06-30 (updated), UTC. Assignment timestamps use their
fixed history dates. Purchases, warranties and history are fixed data, not offsets
from the current day. Structural bootstrap timestamps retain their normal behavior.
An unchanged second startup produces the identical logical state and IDs.
Disabling the flag later stops reseeding; it does not delete previously loaded data.

## Catalogue

Together with bootstrap, there are five types, five manufacturers, thirteen models,
four locations and exactly the five existing reserved statuses.

Additional models, in suffix order: Lenovo ThinkPad T14; HP EliteBook 840;
Apple MacBook Air M2; Dell UltraSharp U2424H; Samsung ViewFinity S6; HP E24 G5;
Apple iPhone 15; Samsung Galaxy A55; HP LaserJet Pro 4002; Dell PowerEdge R350;
HP ProLiant DL20; Lenovo ThinkSystem SR250. Dell Latitude 5450 is retained.
Models do not acquire an artificial type relation. Every informed asset/model
manufacturer pair matches. All catalogue references are active.

No new statuses are created. Nonretired records use ACTIVE, IN_STOCK or IN_REPAIR;
all RETIRED records use reserved RETIRED. Assignments keep the existing LOCATION
semantics and do not manufacture a new assignment/status coupling.

## Canonical equipment

The numbers below also identify the deterministic asset suffix (1000 + number).
A dash means intentionally absent data, not an invalid foreign key.

| # | Tag | Name | Lifecycle | Manufacturer | Location |
| --- | --- | --- | --- | --- | --- |
| 1 | ERB-LT-001 | Operations laptop | READY | Dell | Main office |
| 2 | ERB-LT-002 | Reserve laptop | READY | Lenovo | Warehouse |
| 3 | ERB-LT-003 | Lab service laptop | READY | HP | Technical lab |
| 4 | ERB-LT-004 | Remote design laptop | READY | Apple | Remote office |
| 5 | ERB-LT-005 | Reception laptop | READY | Dell | Main office |
| 6 | ERB-LT-006 | Retired training laptop | RETIRED | Lenovo | Warehouse |
| 7 | — | New laptop awaiting details | DRAFT | — | — |
| 8 | ERB-LT-008 | Laptop awaiting encryption details | DRAFT | HP | — |
| 9 | ERB-MN-001 | Operations display | READY | Dell | Main office |
| 10 | ERB-MN-002 | Reserve widescreen display | READY | Samsung | Warehouse |
| 11 | ERB-MN-003 | Display awaiting backlight repair | READY | HP | Technical lab |
| 12 | ERB-MN-004 | Remote office display | READY | Dell | Remote office |
| 13 | ERB-MN-005 | Display awaiting inspection | DRAFT | Samsung | — |
| 14 | ERB-PH-001 | Operations duty phone | READY | Apple | Main office |
| 15 | ERB-PH-002 | Remote office duty phone | READY | Samsung | Remote office |
| 16 | ERB-PH-003 | Retired demonstration phone | RETIRED | Apple | — |
| 17 | — | Phone awaiting registration | DRAFT | — | — |
| 18 | ERB-PR-001 | Main office document printer | READY | HP | Main office |
| 19 | ERB-PR-002 | Warehouse printer awaiting repair | READY | HP | Warehouse |
| 20 | ERB-PR-003 | Printer awaiting installation | DRAFT | HP | — |
| 21 | ERB-SV-001 | Technical lab application server | READY | Dell | Technical lab |
| 22 | ERB-SV-002 | Reserve integration server | READY | HP | Warehouse |
| 23 | ERB-SV-003 | Remote office file server | READY | Lenovo | Remote office |
| 24 | ERB-SV-004 | Retired training server | RETIRED | Dell | Warehouse |

Completed records have serial numbers, varied condition, purchase dates/prices and
notes. Drafts omit selected optional information; records 7 and 17 are close to the
real minimum CREATE form. No real person, email, credential or API key is included.

## Known facts

These facts describe the canonical 24 rows on a clean demo database, excluding
any subsequently user-created equipment. PostgreSQL integration tests query them
from persisted rows; documentation is not the calculation source.

| Fact | Result |
| --- | --- |
| Total | 24 |
| LAPTOP / MONITOR / PHONE / PRINTER / SERVER | 8 / 5 / 4 / 3 / 4 |
| DRAFT / READY / RETIRED | 5 / 16 / 3 |
| Without location | 6: numbers 7, 8, 13, 16, 17, 20 |
| IN_REPAIR | 3: ERB-LT-003, ERB-MN-003, ERB-PR-002 |
| Condition below 50 | 6: ERB-LT-003, ERB-LT-006, ERB-MN-003, ERB-PH-003, ERB-PR-002, ERB-SV-004 |
| Dell / Lenovo / HP / Samsung / Apple / unspecified | 6 / 3 / 7 / 3 / 3 / 2 |
| Current assignment | 8 assets |
| Assets with closed assignment history | 4 |
| LAPTOP with known RAM below 16 GB | 3: ERB-LT-002, ERB-LT-005, ERB-LT-006 |

## Dynamic fields

LAPTOP gets three active definitions using the existing supported scalar schema:

- RAM_GB: INT32, required, inclusive range 4–128.
- ENCRYPTED: BOOLEAN, required.
- WARRANTY_UNTIL: DATE, optional.

Laptops 1–6 have RAM/encryption pairs 16/true, 8/true, 16/false, 32/true, 8/true,
8/false. Laptops 1, 2, 4 and 5 also have fixed warranty dates. There are 17 values
in total. All READY/RETIRED laptops satisfy required fields.

Draft 7 has neither required value. Draft 8 has RAM=16 but lacks ENCRYPTED.
Thus future questions about missing completion information have objective data,
without any new AI handler or change to AssetDynamicFields. False is a present,
valid boolean, not a missing value. Optional warranties are not required to complete.

## Assignments

All target types are LOCATION. Current assignments start on 2025-02-01 UTC, in
identity order: LT-001 → Main office; LT-002 → Warehouse; LT-004 → Remote office;
LT-005 → Main office; MN-001 → Main office; MN-004 → Remote office;
PH-001 → Main office; SV-001 → Technical lab (all tags have prefix ERB-).

Closed rows, in identity order: LT-001 → Warehouse; LT-003 → Technical lab;
LT-006 → Main office; PH-003 → Main office, from 2024-08-01 to 2025-01-01 UTC,
with explicit closure reasons. LT-001 therefore has a closed period followed by
a later current assignment. No periods overlap, no asset has two current rows,
and no RETIRED asset retains a current assignment. LocationId and custody remain
the existing distinct concepts; demo current targets happen to agree with LocationId.

## Validation and rebuilding

Run normal full validation with the published checkout supplied explicitly:

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/clean-v0.14.0-checkout ./bin/check
```

`AssetsDemoSeedTests` uses real, isolated PostgreSQL databases created and dropped
per test. It exercises migrations → structural seed → optional demo seed, missing
and false flags, SQL facts, references, typed JSONB values, histories, exact snapshot
idempotence, owned normalization, foreign-record preservation, conflict rollback
and PostgreSQL constraints. No EF InMemory provider is used.

For a clean disposable proof, use the owner-managed `bin/validation-db` workflow
with the validation Host stopped: `validation_db_create` recreates only the four
named test databases. An explicit one-off `docker compose run` may enable
`Assets__DemoData__Enabled=true` for that Host; the normal validation service is
unchanged. Stop/remove the one-off Host before calling `validation_db_drop` and
`validation_db_removed`. Do not run this concurrently with `bin/check`.
See [normal validation ownership and cleanup](usage.md#full-validation).

For local development, stop with `./bin/down` and use a dedicated disposable Compose
project/volume if a full demo reset is needed. `docker compose down --volumes` on
that explicitly disposable project removes its database volume and **all data**;
do not use it on a project containing data to preserve. `./bin/up` plus the explicit
demo flag then rebuilds a clean bootstrap/demo dataset. There is no broad demo
DELETE command that could accidentally remove user-created records.

AI-ASSETS-AUDIT-001 and AI-ASSETS-READ-001 remain separate future work.

## Verification — 2026-10-29

`ERBAS_CONTRACT_DIR=/tmp/assets-demo-contract-v0.14.0 ./bin/check` passed:
1,147 .NET tests (177 architecture, 676 Host integration, 181 Resources,
113 Runtime), including eight new PostgreSQL demo cases, with zero failures/skips
and a build with zero warnings/errors. Changed C# files were formatted inside
Docker. OpenAPI validation, published v0.14.0 shared conformance, local Bruno,
persistence/log-secrecy checks and owner database cleanup all passed. Normal
validation remained demo-free. No migration or dependency was added.

A separate explicit opt-in validation Host loaded four fresh disposable databases.
Real SQL and API reads confirmed all facts above, three definitions and seventeen
typed values. Restarting that Host on the same database produced byte-identical
ordered JSONB snapshots of all nine Assets tables, including IDs and timestamps.
The eight dedicated tests also prove normalization and preservation of foreign
records/history, transactional rollback and unchanged structural-only startup.

Unmodified erbas-client `7bcdfbda35a6eeea94ecab767d2750e7c3438b85` ran through its
production Nginx proxy on the Docker network, with Chromium 141.0.7390.37 in the
pinned temporary Playwright 1.56.1 image. Real login and effective navigation opened
the generic Asset register showing 24 rows and the exact contextual columns.
Real API reads independently confirmed type/lifecycle/manufacturer counts, missing
locations, low condition, dynamic values and histories. Completing the minimal
LAPTOP draft correctly returned 409. There were no page runtime errors, response
interception, client changes or new AI behavior. The post-client database snapshot
was also unchanged.

Local evidence is outside the repository under `/tmp/assets-demo-proof/`, including
`facts-first.txt`, `facts-second.txt`, matching snapshots, `client-result.json` and
`assets-list.png`. The temporary Host/proxy and all four databases were removed;
PostgreSQL was returned to its initial stopped state, preserving development data.
