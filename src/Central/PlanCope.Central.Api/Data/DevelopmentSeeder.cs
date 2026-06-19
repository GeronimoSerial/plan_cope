using Microsoft.EntityFrameworkCore;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data;

// Siembra un usuario de testing en desarrollo. Idempotente: no duplica si ya existe.
// Credenciales configurables (seccion "Seed"); el hash se calcula en runtime con el mismo
// BCrypt que valida el login, asi que no queda ningun hash ni secreto en el codigo.
public static class DevelopmentSeeder
{
    public static async Task SeedAsync(
        PlanCopeDbContext dbContext,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var email = (configuration["Seed:AdminEmail"] ?? "admin@plancope.test").Trim();
        var password = configuration["Seed:AdminPassword"] ?? "Admin123!";
        var fullName = configuration["Seed:AdminFullName"] ?? "Administrador de Pruebas";

        var now = DateTimeOffset.UtcNow;

        var role = await dbContext.Roles.FirstOrDefaultAsync(x => x.Code == "Admin", cancellationToken);
        if (role is null)
        {
            role = new Role(NewId(), "Admin", "Administrador", "Acceso completo al builder.", now);
            dbContext.Roles.Add(role);
        }

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
            logger.LogInformation("Seed: usuario de testing creado ({Email}).", email);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var hasAssignment = await dbContext.UserRoles
            .AnyAsync(x => x.UserId == user.Id && x.RoleId == role.Id, cancellationToken);
        if (!hasAssignment)
        {
            dbContext.UserRoles.Add(new UserRoleAssignment(user.Id, role.Id, now));
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
