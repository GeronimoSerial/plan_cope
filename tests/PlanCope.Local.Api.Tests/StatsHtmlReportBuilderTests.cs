using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services.Stats;
using Xunit;

namespace PlanCope.Local.Api.Tests;

public sealed class StatsHtmlReportBuilderTests
{
    private readonly StatsHtmlReportBuilder builder = new();

    [Fact]
    public void Build_escapes_student_and_school_values_in_html()
    {
        var data = new StatsReportDataDto("123456789", "Escuela <script>alert(1)</script>",
            [new StatsReportAttemptDto("Ana <img src=x onerror=alert(1)>", "1234", "6", "A", "exam-1", "MAT&6", 42, null, null)], 1, 1);

        var html = builder.Build(data, [], "2026", null, null, DateTimeOffset.Parse("2026-09-30T12:00:00Z"));

        Assert.Contains("Escuela &lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("Ana &lt;img src=x onerror=alert(1)&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.DoesNotContain("<img src=x", html);
    }

    [Fact]
    public void Build_shows_clear_empty_state_when_no_attempts_exist()
    {
        var data = new StatsReportDataDto("123456789", "Escuela", [], 0, 0);

        var html = builder.Build(data, [], null, null, null, DateTimeOffset.UtcNow);

        Assert.Contains("Sin intentos entregados", html);
        Assert.Contains("aplicados", html);
        Assert.DoesNotContain("Distribución de puntajes", html);
    }

    [Fact]
    public void Build_suppresses_student_details_for_small_non_school_cohort()
    {
        var attempts = Enumerable.Range(1, 4).Select(index => new StatsReportAttemptDto($"Alumno {index}", $"{index:0000}", "6", "A", "exam-1", "MAT-6", index * 10, null, null)).ToArray();
        var data = new StatsReportDataDto("123456789", "Escuela", attempts, 1, 4);

        var html = builder.Build(data, [], "2026", "6", null, DateTimeOffset.UtcNow, "course");

        Assert.Contains("cohorte insuficiente", html);
        Assert.DoesNotContain("Alumno 1", html);
        Assert.DoesNotContain("0001", html);
    }
}
