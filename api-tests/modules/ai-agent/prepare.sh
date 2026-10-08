#!/usr/bin/env bash
set -euo pipefail

# Test-only identity. Never add this fixture to production/module seed permissions.
[[ "$(docker compose exec -T validation-app printenv ASPNETCORE_ENVIRONMENT)" == Testing ]] || {
    echo "ERROR: write-only fixture requires the Testing Host" >&2
    exit 1
}
docker compose exec -T postgres psql -U alxarafe -d alxarafe_security_test -v ON_ERROR_STOP=1 <<'SQL'
BEGIN;
DO $$
DECLARE
    fixture_id uuid;
BEGIN
    IF current_database() <> 'alxarafe_security_test' THEN
        RAISE EXCEPTION 'Write-only fixture requires the isolated security database';
    END IF;
    SELECT "Id" INTO fixture_id FROM "AspNetUsers" WHERE "NormalizedUserName" = 'AI-WRITE-ONLY@EXAMPLE.TEST';
    IF fixture_id IS NULL THEN
        -- The standard Identity password hash is user-independent. Reuse only the
        -- seeded test credential hash; identity, stamps and claims remain distinct.
        INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
            "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "PhoneNumberConfirmed",
            "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
        SELECT gen_random_uuid(), 'ai-write-only@example.test', 'AI-WRITE-ONLY@EXAMPLE.TEST',
            'ai-write-only@example.test', 'AI-WRITE-ONLY@EXAMPLE.TEST', true, "PasswordHash",
            gen_random_uuid()::text, gen_random_uuid()::text, false, false, true, 0
        FROM "AspNetUsers" WHERE "NormalizedUserName" = 'AI-WRITER@EXAMPLE.TEST' AND "PasswordHash" IS NOT NULL
        RETURNING "Id" INTO fixture_id;
        IF fixture_id IS NULL THEN RAISE EXCEPTION 'Seeded writer fixture is missing'; END IF;
        INSERT INTO "AspNetUserClaims" ("UserId", "ClaimType", "ClaimValue")
            VALUES (fixture_id, 'permission', 'ai.knowledge.write');
    END IF;
    IF (SELECT count(*) FROM "AspNetUserClaims" WHERE "UserId" = fixture_id) <> 1
        OR NOT EXISTS (SELECT 1 FROM "AspNetUserClaims" WHERE "UserId" = fixture_id
            AND "ClaimType" = 'permission' AND "ClaimValue" = 'ai.knowledge.write')
        OR EXISTS (SELECT 1 FROM "AspNetUserRoles" WHERE "UserId" = fixture_id) THEN
        RAISE EXCEPTION 'Write-only fixture has unexpected claims or roles';
    END IF;
END $$;
COMMIT;
SQL
