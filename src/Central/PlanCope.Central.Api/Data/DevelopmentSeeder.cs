using Microsoft.EntityFrameworkCore;
using PlanCope.Shared.Domain.Central;

namespace PlanCope.Central.Api.Data;

// Siembra un usuario de testing en desarrollo. Idempotente: no duplica si ya existe.
// Credenciales configurables (seccion "Seed"); el hash se calcula en runtime con el mismo
// BCrypt que valida el login, asi que no queda ningun hash ni secreto en el codigo.
public static class DevelopmentSeeder
{
    private const string DemoCue = "180000100";

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

        var provinceRole = await dbContext.Roles.FirstOrDefaultAsync(x => x.Code == "RosterProvince", cancellationToken);
        if (provinceRole is null)
        {
            provinceRole = new Role(
                NewId(),
                "RosterProvince",
                "Alcance provincial de padrón",
                "Acceso de lectura a los 1.440 CUEs sin fila en user_schools (decisión 13).",
                now);
            dbContext.Roles.Add(provinceRole);
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

        // Admin's primary role has school roster scope. Without an assigned CUE the
        // development account cannot issue activation keys or inspect a roster.
        if (!await dbContext.UserSchools.AnyAsync(x => x.UserId == user.Id && x.Cue == DemoCue, cancellationToken))
        {
            dbContext.UserSchools.Add(new UserSchoolAssignment(user.Id, DemoCue, now));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await SeedDemoSchoolAsync(dbContext, now, cancellationToken);
    }

    private static async Task SeedDemoSchoolAsync(PlanCopeDbContext dbContext, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string provinceId = "dev-corrientes";
        const string departmentId = "dev-department";
        const string localityId = "dev-locality";
        const string schoolId = "dev-school-180000100";

        if (!await dbContext.Provinces.AnyAsync(x => x.Id == provinceId, cancellationToken))
            dbContext.Provinces.Add(new Province(provinceId, "18", "Corrientes (demo)", now));
        if (!await dbContext.Departments.AnyAsync(x => x.Id == departmentId, cancellationToken))
            dbContext.Departments.Add(new Department(departmentId, "DEMO", "Departamento demo", provinceId, now));
        if (!await dbContext.Localities.AnyAsync(x => x.Id == localityId, cancellationToken))
            dbContext.Localities.Add(new Locality(localityId, departmentId, "DEMO", null, "Localidad demo", now));
        if (!await dbContext.Schools.AnyAsync(x => x.Cue == 180000100, cancellationToken))
            dbContext.Schools.Add(new School(schoolId, "DEMO-001", 180000100, 0, "Escuela demo local", localityId, "Active", null, now, now));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
