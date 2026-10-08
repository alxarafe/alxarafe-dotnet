#!/usr/bin/env bash
set -euo pipefail

# Module assertions run only against the databases prepared by bin/check.
source ./bin/validation-db
echo "==> Checking AiAgent OpenAPI, migration, and persistence"
openapi="$(docker compose exec -T validation-app curl --fail --silent --show-error http://localhost:8080/openapi/v1.json)"
grep -Fq '"/api/ai/knowledge"' <<<"$openapi"
grep -Fq '"/api/ai/knowledge/{id}"' <<<"$openapi"
docker compose exec -T postgres psql -U alxarafe -d "$validation_ai_db" -v ON_ERROR_STOP=1 -tAc \
    "SELECT string_agg(column_name, ',' ORDER BY ordinal_position) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'ai_knowledge'" | grep -Fxq 'Id,Question,Answer'
docker compose exec -T postgres psql -U alxarafe -d "$validation_ai_db" -v ON_ERROR_STOP=1 -tAc \
    "SELECT 1 FROM ai_knowledge WHERE \"Question\" LIKE 'Bruno knowledge question %' AND \"Answer\" = 'Bruno knowledge answer'" | grep -Fxq '1'
docker compose exec -T postgres psql -U alxarafe -d "$validation_ai_db" -v ON_ERROR_STOP=1 -tAc \
    "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"" | grep -Fxq '202610060001_InitialKnowledge'
