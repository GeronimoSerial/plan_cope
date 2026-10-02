using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Shared.Contracts.Stats;
using PlanCope.Shared.Domain.Central;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class StatsSharePostgresIntegrationTests
{
    [DockerFact]
    public async Task UpgradeDatabase_StoresAndReadsAnImmutableSqlAggregateSnapshot()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseNpgsql(postgres.GetConnectionString(), provider => provider.MigrationsAssembly(
                typeof(PlanCope.Central.Migrations.Migrations.AddDeliverySessionNodeOwnership).Assembly.GetName().Name))
            .Options;

        await using (var migrationContext = new PlanCopeDbContext(options))
        {
            var previousMigration = migrationContext.Database.GetMigrations()
                .Where(migration => !migration.EndsWith("AddStatsShares", StringComparison.Ordinal))
                .Last();
            await migrationContext.GetService<IMigrator>().MigrateAsync(previousMigration);
            await migrationContext.Database.MigrateAsync();
            Assert.False(migrationContext.Database.HasPendingModelChanges());
            migrationContext.ExamRollups.Add(new ExamRollup("share-rollup-1", "180000100", "2026", "4° grado", "published-version-1", 8, 54, 80, DateTimeOffset.UtcNow));
            await migrationContext.SaveChangesAsync();
        }

        await using var db = new PlanCopeDbContext(options);
        var (created, error) = await new StatsShareService(db).CreateAsync(
            new StatsShareCreateRequest("course", new Dictionary<string, string?> { ["schoolYear"] = "2026" }),
            "admin-1", null, "https://central.example", null, CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(created);
        Assert.StartsWith("https://central.example/estadisticas/compartidas/", created.Url);
        var token = created.Url.Split('/').Last();
        var snapshot = await new StatsShareService(db).FindPublicAsync(token, CancellationToken.None);
        var row = Assert.Single(snapshot!.Rows);
        Assert.Equal("4° grado", row.Label);
        Assert.Equal(8, row.AttemptCount.Value);
        Assert.Equal(67.5, row.WeightedScorePercent.Value);
        Assert.Equal(8, snapshot.TotalAttempts.Value);

        db.ExamRollups.Add(new ExamRollup("share-rollup-2", "180000200", "2026", "4° grado", "published-version-2", 8, 8, 16, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var afterNewRollup = await new StatsShareService(db).FindPublicAsync(token, CancellationToken.None);
        Assert.Equal(67.5, Assert.Single(afterNewRollup!.Rows).WeightedScorePercent.Value);
    }
}
