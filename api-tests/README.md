# Local API verification

The active local collection is api-tests/platform: three requests covering Health,
.NET-only Identity registration and Spanish validation with stable technical keys.
Shared Health/Auth/Users/Modules conformance belongs exclusively to erbas-contract.
No business collection is currently installed.

```bash
ERBAS_CONTRACT_DIR=/absolute/path/to/clean-v0.5.0-checkout ./bin/check
./bin/bruno platform
```

Bruno runs inside Docker on the validation network. Platform generates its own
unique registration emails and does not need seeded module identities. bin/check
supplies a disposable CORE administrator to the shared runner and validates
persistence, secrecy and cleanup. Native tests complement Bruno with generic
permissions, dynamic canonical gating and en/es/default/regional/fallback cases.

The existing runner discovers future module collections by bruno.json. Functional
modules must own any additional fixtures, SQL/OpenAPI assertions and local extension
scenarios. Common contractual behavior belongs in the single shared collection.
Do not add business examples or duplicate shared conformance here.
