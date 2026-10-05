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
}
