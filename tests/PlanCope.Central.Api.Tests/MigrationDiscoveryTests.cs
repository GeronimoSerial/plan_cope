using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanCope.Central.Migrations.Migrations;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class MigrationDiscoveryTests
{
    [Fact]
    public void Nominal_sync_migration_has_ef_discovery_metadata()
    {
        var migrationType = typeof(AddNominalAttemptSync);

        Assert.NotNull(migrationType.GetCustomAttributes(typeof(DbContextAttribute), inherit: false).SingleOrDefault());
        var migration = migrationType.GetCustomAttributes(typeof(MigrationAttribute), inherit: false).Cast<MigrationAttribute>().Single();
        Assert.Equal("20260820170000_AddNominalAttemptSync", migration.Id);
    }
}
