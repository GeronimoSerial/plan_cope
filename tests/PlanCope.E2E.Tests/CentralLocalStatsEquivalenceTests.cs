using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PlanCope.Central.Api.Auth;
using PlanCope.Central.Api.Controllers;
using PlanCope.Central.Api.Data;
using PlanCope.Central.Api.Services;
using PlanCope.Local.Api.Data;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Central;
using PlanCope.Shared.Grading;
using PlanCope.TestSupport;
using Xunit;

namespace PlanCope.E2E.Tests;

public sealed class CentralLocalStatsEquivalenceTests
{
    private const string Cue = "180000100";
    private const string SchoolYear = "2026";
    private const string Course = "6to A";
    private const string ExamVersionId = "ev-equiv-1";
    private const int ExpectedAttemptCount = 1;
    private const double ExpectedAverageScorePercent = 80.0;
    private const string BlocksJson = "[{\"BlockId\":\"blk-1\",\"Outcome\":\"Correct\",\"Score\":8,\"ScoreMax\":10}]";

    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Central_and_local_statistics_match_for_the_same_school_exam_and_schema_version()
    {
        var local = await ReadLocalSchoolStatsAsync();
        var central = await ReadCentralSchoolStatsAsync();

        Assert.False(local.AttemptCount.IsSuppressed, "Local attempt count was suppressed; the equivalence assertion needs the real numbers.");
        Assert.False(local.AverageScorePercent.IsSuppressed, "Local average score percent was suppressed; the equivalence assertion needs the real numbers.");

        var localAttemptCount = local.AttemptCount.Value;
        var localAverageScorePercent = local.AverageScorePercent.Value;

        Assert.Equal(ExpectedAttemptCount, localAttemptCount);
        Assert.Equal(ExpectedAverageScorePercent, localAverageScorePercent, 3);
        Assert.Equal(ExpectedAttemptCount, central.AttemptCount);
        Assert.Equal(ExpectedAverageScorePercent, central.AverageScorePercent, 3);

        Assert.Equal(central.AttemptCount, localAttemptCount);
        Assert.Equal(central.AverageScorePercent, localAverageScorePercent, 3);
    }

    private static async Task<SchoolStatsDto> ReadLocalSchoolStatsAsync()
    {
        using var database = new LocalStatsDatabase();
        var connectionFactory = database.CreateConnectionFactory();
        await SeedLocalAttemptAsync(connectionFactory);

        var rollupRepository = new StatsRollupRepository(connectionFactory, NullLogger<StatsRollupRepository>.Instance);
        await rollupRepository.UpsertForAttemptAsync("attempt-equiv-1");

        var queryRepository = new StatsQueryRepository(connectionFactory);
        return await queryRepository.GetSchoolStatsAsync(Cue, "school", SchoolYear, Course);
    }

    private static async Task<(int AttemptCount, double AverageScorePercent)> ReadCentralSchoolStatsAsync()
    {
        var options = CreateOptions();
        using var dbContext = new PlanCopeDbContext(options);
        await SeedCentralAttemptAsync(dbContext);

        await new CentralStatsRollupService(dbContext).UpsertForAttemptAsync(
            "attempt-equiv-1",
            new AttemptResult
            {
                ExamVersionId = ExamVersionId,
                Score = 8m,
                ScoreMax = 10m,
                Blocks = new[]
                {
                    new BlockResult
                    {
                        BlockId = "blk-1",
                        BlockType = BlockType.MultipleChoice,
                        Outcome = BlockOutcome.Correct,
                        Score = 8m,
                        ScoreMax = 10m
                    }
                }
            },
            ExamVersionId);

        using var scope = CreateAuthScope();
        var controller = CreateController(
            dbContext,
            scope.ServiceProvider.GetRequiredService<IAuthorizationService>(),
            SchoolPrincipal(Cue));

        var result = await controller.GetSchool(Cue, SchoolYear, Course, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        using var json = ToJson(ok.Value);

        Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("attemptCount").ValueKind);
        Assert.Equal(JsonValueKind.Number, json.RootElement.GetProperty("averageScorePercent").ValueKind);

        return (
            json.RootElement.GetProperty("attemptCount").GetInt32(),
            json.RootElement.GetProperty("averageScorePercent").GetDouble());
    }

    private static async Task SeedLocalAttemptAsync(LocalSqliteConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        Execute(connection, transaction, """
            INSERT INTO schools (cue, name, created_at) VALUES (@cue, 'Escuela Equivalencia', @now);
            """,
            ("@cue", Cue),
            ("@now", Now.ToString("O")));

        Execute(connection, transaction, """
            INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at)
            VALUES (@id, 'remote-ev-equiv-1', 'MAT-6', 1, 'chk', '{}', 1, @now);
            """,
            ("@id", ExamVersionId),
            ("@now", Now.ToString("O")));

        Execute(connection, transaction, """
            INSERT INTO local_roster_snapshots (id, cue, school_year, fetched_at, checksum, section_count, student_count, status)
            VALUES ('snap-equiv-1', @cue, @schoolYear, @now, 'chk', 1, 1, 'current');
            """,
            ("@cue", Cue),
            ("@schoolYear", SchoolYear),
            ("@now", Now.ToString("O")));

        Execute(connection, transaction, """
            INSERT INTO local_roster_sections (id, snapshot_id, ge_section_id, course, division, level, shift)
            VALUES ('sec-equiv-1', 'snap-equiv-1', NULL, @course, NULL, NULL, NULL);
            """,
            ("@course", Course));

        Execute(connection, transaction, """
            INSERT INTO delivery_sessions (id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count, school_year, roster_snapshot_id, roster_section_id)
            VALUES ('ds-equiv-1', @examVersionId, @cue, NULL, NULL, 'test', @now, NULL, 'closed', NULL, 'EQV111', 1, @schoolYear, 'snap-equiv-1', 'sec-equiv-1');
            """,
            ("@examVersionId", ExamVersionId),
            ("@cue", Cue),
            ("@now", Now.ToString("O")),
            ("@schoolYear", SchoolYear));

        Execute(connection, transaction, """
            INSERT INTO student_attempts (id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence, confirmation_code)
            VALUES ('attempt-equiv-1', 'ds-equiv-1', 'student-1', 'submitted', @now, @now, 1, 'CONF-EQV-1');
            """,
            ("@now", Now.ToString("O")));

        Execute(connection, transaction, """
            INSERT INTO attempt_results (id, student_attempt_id, grading_schema_version, scoring_policy, status, score, score_max, blocks_json, graded_at)
            VALUES ('result-equiv-1', 'attempt-equiv-1', 1, 'AllOrNothing', 'graded', 8.0, 10.0, @blocksJson, @now);
            """,
            ("@blocksJson", BlocksJson),
            ("@now", Now.ToString("O")));

        transaction.Commit();
    }

    private static async Task SeedCentralAttemptAsync(PlanCopeDbContext dbContext)
    {
        dbContext.GeRosterSnapshots.Add(new GeRosterSnapshot
        {
            Id = "snap-equiv-1",
            Cue = Cue,
            SchoolYear = SchoolYear,
            FetchedAt = Now,
            Checksum = "chk",
            SectionCount = 1,
            StudentCount = 1,
            Status = "current"
        });
        dbContext.GeRosterSections.Add(new GeRosterSection
        {
            Id = "sec-equiv-1",
            SnapshotId = "snap-equiv-1",
            Course = Course
        });
        dbContext.DeliverySessions.Add(new CentralDeliverySession(
            "ds-equiv-1",
            "ds-equiv-1-local",
            Cue,
            ExamVersionId,
            null,
            null,
            "closed",
            Now,
            null,
            Now,
            Now));
        dbContext.ReceivedStudentAttempts.Add(new ReceivedStudentAttempt(
            "attempt-equiv-1",
            "attempt-equiv-1-local",
            "ds-equiv-1",
            "student-1",
            "submitted",
            Now,
            Now,
            Now,
            "idem-equiv-1",
            Now,
            RosterSectionId: "sec-equiv-1"));

        await dbContext.SaveChangesAsync();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private static readonly IServiceProvider InMemoryServices = new ServiceCollection()
        .AddEntityFrameworkInMemoryDatabase()
        .AddSingleton<IModelCustomizer, JsonDocumentFriendlyModelCustomizer>()
        .BuildServiceProvider();

    private static DbContextOptions<PlanCopeDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<PlanCopeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .UseInternalServiceProvider(InMemoryServices)
            .Options;
    }

    private static StatsController CreateController(
        PlanCopeDbContext dbContext,
        IAuthorizationService authorizationService,
        ClaimsPrincipal principal)
    {
        return new StatsController(dbContext, authorizationService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };
    }

    private static IServiceScope CreateAuthScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("RosterCueAccess", policy => policy.Requirements.Add(new RosterScopeRequirement()));
        });
        services.AddScoped<IAuthorizationHandler, RosterScopeAuthorizationHandler>();
        return services.BuildServiceProvider().CreateScope();
    }

    private static ClaimsPrincipal SchoolPrincipal(string cue)
    {
        return Principal(
            new Claim("roster_scope", "school"),
            new Claim("roster_cue", cue));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static JsonDocument ToJson(object? value)
    {
        return JsonDocument.Parse(JsonSerializer.Serialize(value));
    }

    private sealed class LocalStatsDatabase : IDisposable
    {
        private readonly string databasePath;
        private readonly string connectionString;

        public LocalStatsDatabase()
        {
            databasePath = Path.Combine(Path.GetTempPath(), $"plancope-stats-equivalence-{Guid.NewGuid():N}.db");
            connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
            new LocalDatabaseInitializer(new LocalDatabaseOptions(connectionString)).Initialize();
        }

        public LocalSqliteConnectionFactory CreateConnectionFactory()
        {
            return new LocalSqliteConnectionFactory(new LocalDatabaseOptions(connectionString));
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
