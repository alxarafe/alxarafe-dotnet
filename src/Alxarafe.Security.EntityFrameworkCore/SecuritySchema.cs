using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Security.EntityFrameworkCore;

public static class SecuritySchema
{
    // Shared email has no contractual maximum. EnsureCreated cannot upgrade the
    // previous Identity varchar(256) columns; this bounded, idempotent change can.
    public static Task UpgradeEmailStorageAsync(SecurityDbContext database) => database.Database.ExecuteSqlRawAsync("""
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_schema = current_schema() AND table_name = 'AspNetUsers'
                       AND column_name IN ('Email', 'NormalizedEmail') AND data_type <> 'text') THEN
                ALTER TABLE "AspNetUsers" ALTER COLUMN "Email" TYPE text,
                                         ALTER COLUMN "NormalizedEmail" TYPE text;
            END IF;
        END $$;
        """);
}
