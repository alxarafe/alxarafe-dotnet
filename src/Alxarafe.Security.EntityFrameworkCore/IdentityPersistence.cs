using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Security.EntityFrameworkCore;

public sealed class AlxarafeUser : IdentityUser<Guid>
{
}

public sealed class SecurityDbContext(DbContextOptions<SecurityDbContext> options)
    : IdentityDbContext<AlxarafeUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<AlxarafeUser>().Property(user => user.Email).HasColumnType("text").Metadata.SetMaxLength(null);
        builder.Entity<AlxarafeUser>().Property(user => user.NormalizedEmail).HasColumnType("text").Metadata.SetMaxLength(null);
    }
}
