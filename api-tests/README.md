# Bruno API tests

The collection in `bruno/` is complementary to the .NET unit, architecture, and integration tests. Select the `local` environment and run the requests in sequence against the Docker application at `http://localhost:8081`.

The collection uses only development credentials. The login scripts store the returned bearer token in the `token` variable and the create request stores its identifier in `itemId`. Do not reuse these credentials outside local development.

Run all 14 requests without installing Bruno, Node.js, or the .NET SDK on the host:

```bash
docker compose run --rm bruno
```

The Docker-only environment points at the `app` service. For a Bruno CLI running outside Compose, use the `local` environment (`http://localhost:8081`).
