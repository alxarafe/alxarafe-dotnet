#!/usr/bin/env bash
set -euo pipefail

# Called by bin/check with the isolated validation Host and databases prepared.
source ./bin/validation-db
echo "==> Checking Catalog OpenAPI and persistence"
openapi="$(docker compose exec -T validation-app curl --fail --silent --show-error http://localhost:8080/openapi/v1.json)"
grep -Fq '"/api/catalog/items' <<<"$openapi"
docker compose exec -T postgres psql -U alxarafe -d "$validation_catalog_db" -tAc \
    "SELECT to_regclass('public.catalog_items')" | grep -Fxq 'catalog_items'
docker compose exec -T postgres psql -U alxarafe -d "$validation_catalog_db" -tAc \
    "SELECT 1 FROM catalog_items WHERE \"Name\" = 'Bruno item'" | grep -Fxq '1'
docker compose exec -T postgres psql -U alxarafe -d "$validation_security_db" -tAc \
    "SELECT COUNT(*) FROM \"AspNetUsers\" WHERE \"Email\" IN ('reader@example.test', 'creator@example.test')" | awk '{ if ($1 < 2) exit 1 }'
