using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using PlanCope.Central.Migrations.Migrations;
using System.Reflection;
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

    [Fact]
    public void Package_uniqueness_migration_has_ef_discovery_metadata()
    {
        var migrationType = typeof(EnforceOnePackagePerExamVersion);

        Assert.NotNull(migrationType.GetCustomAttributes(typeof(DbContextAttribute), inherit: false).SingleOrDefault());
        var migration = migrationType.GetCustomAttributes(typeof(MigrationAttribute), inherit: false).Cast<MigrationAttribute>().Single();
        Assert.Equal("20260929120000_EnforceOnePackagePerExamVersion", migration.Id);
    }

    [Fact]
    public void Package_uniqueness_migration_seeds_the_assignable_exam_author_role_and_repairs_duplicates_first()
    {
        var instance = Activator.CreateInstance(typeof(EnforceOnePackagePerExamVersion));
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(EnforceOnePackagePerExamVersion)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(instance, [builder]);

        var sqlOperations = builder.Operations.OfType<SqlOperation>().ToList();
        var operations = builder.Operations.ToList();
        Assert.Contains(sqlOperations, operation => operation.Sql.Contains("'ExamAuthor'", StringComparison.Ordinal));
        Assert.Contains(sqlOperations, operation => operation.Sql.Contains("publication.targets", StringComparison.Ordinal));
        Assert.True(operations.FindIndex(operation => operation is CreateIndexOperation) >
                    operations.FindLastIndex(operation => operation is SqlOperation));
    }
}
