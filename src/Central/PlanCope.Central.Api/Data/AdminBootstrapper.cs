using Microsoft.EntityFrameworkCore;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data;

// Creates the first production administrator account from configuration (B1).
// Opt-in: when none of the three PLANCOPE_BOOTSTRAP_ADMIN_* variables is
// configured, the run is an ordinary restart — it logs at Information level and
// returns without throwing or touching the database. When any of them is set
// (even partially), a bootstrap was requested: missing/weak/default values then
// throw InvalidOperationException so a half-configured deployment crashes at
// startup instead of coming up with no way to log in. Idempotent: an existing
// user is never modified (PasswordHash and every other field stay untouched);
// only the Admin role assignment is ensured, which also heals a half-applied
// previous run. The password and its hash are never logged on any path.
public static class AdminBootstrapper
{
    private const string EmailVariable = "PLANCOPE_BOOTSTRAP_ADMIN_EMAIL";
    private const string PasswordVariable = "PLANCOPE_BOOTSTRAP_ADMIN_PASSWORD";
    private const string FullNameVariable = "PLANCOPE_BOOTSTRAP_ADMIN_FULL_NAME";
    private const int MinimumPasswordLength = 12;

    public static async Task BootstrapAsync(
        PlanCopeDbContext dbContext,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var rawEmail = configuration[EmailVariable];
        var rawPassword = configuration[PasswordVariable];
        var rawFullName = configuration[FullNameVariable];

        // Opt-in bootstrap: all three variables absent/blank means no bootstrap
        // was requested at all — this is the path every ordinary restart takes.
        // Return silently without throwing and without creating any user.
        if (string.IsNullOrWhiteSpace(rawEmail) &&
            string.IsNullOrWhiteSpace(rawPassword) &&
            string.IsNullOrWhiteSpace(rawFullName))
        {
            logger.LogInformation(
                "Admin bootstrap is not configured ({EmailVariable}, {PasswordVariable}, {FullNameVariable} all unset); skipping.",
                EmailVariable,
                PasswordVariable,
                FullNameVariable);
            return;
        }

        var email = (rawEmail ?? string.Empty).Trim();
        var password = rawPassword ?? string.Empty;
        var fullName = (rawFullName ?? string.Empty).Trim();

        // A partial/half-configured set means a bootstrap was requested but got
        // it wrong — fail loudly on every run (not only the first), never fall
        // back to a default password. Messages name the offending variable but
        // never its value.
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(
                $"{EmailVariable} is missing or blank. Set it to the administrator email address.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                $"{PasswordVariable} is missing or blank. Set it to a strong password of at least {MinimumPasswordLength} characters.");
        }

        if (password.Length < MinimumPasswordLength)
        {
            throw new InvalidOperationException(
                $"{PasswordVariable} is too short. It must be at least {MinimumPasswordLength} characters.");
        }

        if (string.Equals(password, "Admin123!", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(password, "password", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{PasswordVariable} is a known default password and is rejected. Choose a different password.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException(
                $"{FullNameVariable} is missing or blank. Set it to the administrator display name.");
        }

        var now = DateTimeOffset.UtcNow;
        var created = false;

        var user = await dbContext.Users.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null)
        {
            user = new User(
                NewId(),
                email,
                BCrypt.Net.BCrypt.HashPassword(password),
                fullName,
                "Active",
                null,
                null,
                now,
                now);
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            created = true;
        }

        // Ensure the Admin role exists and is assigned (same look-up-by-Code
        // pattern as DevelopmentSeeder). This is a no-op when everything is
        // already in place and heals a missing assignment on re-run.
        var role = await dbContext.Roles.FirstOrDefaultAsync(x => x.Code == "Admin", cancellationToken);
        if (role is null)
        {
            role = new Role(NewId(), "Admin", "Administrator", "Full access to the builder.", now);
            dbContext.Roles.Add(role);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var hasAssignment = await dbContext.UserRoles
            .AnyAsync(x => x.UserId == user.Id && x.RoleId == role.Id, cancellationToken);
        if (!hasAssignment)
        {
            dbContext.UserRoles.Add(new UserRoleAssignment(user.Id, role.Id, now));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (created)
        {
            logger.LogInformation("Admin bootstrap: created administrator account ({Email}).", email);
        }
        else
        {
            logger.LogDebug("Admin bootstrap: administrator account ({Email}) already exists; Admin role ensured.", email);
        }
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
