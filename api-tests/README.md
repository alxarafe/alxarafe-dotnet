# Bruno API tests

The collection in `bruno/` complements the .NET unit, architecture, and integration tests. It targets the isolated validation Host at `http://validation-app:8080` inside Compose.

The collection uses test-only credentials. Each run generates a unique registration email and catalog SKU; the login scripts store the returned bearer token in the `token` variable and the create request stores its identifier in `itemId`.

Run all 14 requests without installing Bruno, Node.js, or the .NET SDK on the host:

```bash
./bin/bruno
```

`bin/bruno` creates `alxarafe_test` and `alxarafe_security_test`, starts the validation Host, runs the collection and removes the test databases even after a failure. `./bin/check` runs the same collection after the .NET tests.
