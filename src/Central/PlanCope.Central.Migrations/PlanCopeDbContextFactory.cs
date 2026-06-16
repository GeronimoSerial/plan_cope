using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PlanCope.Central.Api.Data;

namespace PlanCope.Central.Migrations;

public sealed class PlanCopeDbContextFactory : IDesignTimeDbContextFactory<PlanCopeDbContext>
{
    public PlanCopeDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("PLANCOPE_CENTRAL_DB") ??
                               "Host=localhost;Port=5432;Database=plan_cope_central;Username=plancope;Password=plancope";

        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PlanCopeDbContext(options);
    }
}
