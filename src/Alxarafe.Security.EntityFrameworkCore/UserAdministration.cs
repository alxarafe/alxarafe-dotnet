using System.Data;
using Alxarafe.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alxarafe.Security.EntityFrameworkCore;

public sealed class UserAdministration(SecurityDbContext database, UserManager<AlxarafeUser> users)
{
    private IQueryable<CoreUserState> States => Project(database.Users);
    private IQueryable<CoreUserState> Project(IQueryable<AlxarafeUser> source) => source.AsNoTracking().Select(user => new CoreUserState
    {
        Id = user.Id,
        Email = user.Email!,
        Enabled = !database.UserClaims.Any(claim => claim.UserId == user.Id && claim.ClaimType == CoreUserClaims.Enabled && claim.ClaimValue == "false"),
        Admin = database.UserClaims.Any(claim => claim.UserId == user.Id && claim.ClaimType == CoreUserClaims.Admin && claim.ClaimValue == "true")
    });

    public Task<CoreUserState?> FindAsync(Guid id) => States.SingleOrDefaultAsync(user => user.Id == id);
    public async Task<AlxarafeUser?> FindLoginUserAsync(string email)
    {
        var exact = await database.Users.SingleOrDefaultAsync(user => user.Email == email);
        if (exact is not null) return exact;
        var normalized = users.NormalizeEmail(email);
        var matches = await database.Users.Where(user => user.NormalizedEmail == normalized).Take(2).ToListAsync();
        // Retain Identity's normalization for unambiguous legacy lookups without
        // confusing distinct contract-created exact email identities.
        return matches.Count == 1 ? matches[0] : null;
    }
    public Task<long> CountAsync() => database.Users.LongCountAsync();
    public Task<List<CoreUserState>> ListAsync(long offset, int limit) => Project(database.Users.FromSqlInterpolated(
        $"SELECT * FROM \"AspNetUsers\" ORDER BY \"Id\" ASC LIMIT {limit} OFFSET {offset}"))
        .OrderBy(user => user.Id).ToListAsync();

    // NULL represents an exact-email conflict. The native Identity hasher and
    // normalizer remain shared with login/registration, but their input validators do not.
    public async Task<CoreUserState?> CreateAsync(string email, string password, bool admin)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockAsync();
        if (await database.Users.AnyAsync(user => user.Email == email)) return null;
        var id = Guid.NewGuid();
        var user = new AlxarafeUser
        {
            Id = id,
            UserName = id.ToString(),
            NormalizedUserName = users.NormalizeName(id.ToString()),
            Email = email,
            NormalizedEmail = users.NormalizeEmail(email),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };
        user.PasswordHash = users.PasswordHasher.HashPassword(user, password);
        database.Users.Add(user);
        database.UserClaims.Add(new IdentityUserClaim<Guid> { UserId = id, ClaimType = CoreUserClaims.Admin, ClaimValue = admin ? "true" : "false" });
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return new CoreUserState { Id = id, Email = email, Enabled = true, Admin = admin };
    }

    public async Task<UserUpdateResult> UpdateAsync(Guid id, bool? enabled, bool? admin)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await LockAsync();
        var user = await FindAsync(id);
        if (user is null) return new(UserUpdateOutcome.NotFound);
        var updated = user with { Enabled = enabled ?? user.Enabled, Admin = admin ?? user.Admin };
        if (user.Enabled && user.Admin && !(updated.Enabled && updated.Admin)
            && !await States.AnyAsync(other => other.Id != id && other.Enabled && other.Admin))
            return new(UserUpdateOutcome.LastAdmin);

        // Refresh tracked claim rows after serialization; never retain an earlier snapshot.
        var claims = await database.UserClaims.Where(claim => claim.UserId == id &&
            (claim.ClaimType == CoreUserClaims.Enabled || claim.ClaimType == CoreUserClaims.Admin)).ToListAsync();
        database.UserClaims.RemoveRange(claims);
        database.UserClaims.AddRange(
            new IdentityUserClaim<Guid> { UserId = id, ClaimType = CoreUserClaims.Enabled, ClaimValue = updated.Enabled ? "true" : "false" },
            new IdentityUserClaim<Guid> { UserId = id, ClaimType = CoreUserClaims.Admin, ClaimValue = updated.Admin ? "true" : "false" });
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(UserUpdateOutcome.Updated, updated);
    }

    // One PostgreSQL serialization point for CORE creation/state changes and
    // controlled registration. Read-only authentication is not blocked.
    public Task LockAsync() => database.Database.ExecuteSqlRawAsync(
        "LOCK TABLE \"AspNetUsers\", \"AspNetUserClaims\" IN SHARE ROW EXCLUSIVE MODE");
}
