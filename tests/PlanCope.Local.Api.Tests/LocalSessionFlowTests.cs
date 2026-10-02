using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using PlanCope.Shared.Contracts.Local;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.Local;
using Xunit;
using Xunit.Abstractions;

namespace PlanCope.Local.Api.Tests;

public sealed class LocalSessionFlowTests
{
    private readonly ITestOutputHelper output;

    public LocalSessionFlowTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task Revoked_but_unlocked_node_cannot_create_a_new_session()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedRevokedNode();

        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055400", "6 A", null, "Operador", 0, null));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
        Assert.Equal("node_revoked", body.GetProperty("errorCode").GetString());
        Assert.Contains("clave nueva", body.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Node_sessions_include_school_exam_grade_and_counters_and_history_filters_paginate()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "student-a", 501, "12.345.678", "Ana", "Pérez");
        factory.SeedRosterStudent("snapshot-a", "section-a", "student-b", 502, "23.456.789", "Luis", "Gómez");
        factory.SetSchoolName("180055400", "Escuela Norte");
        var open = await CreateRosterSessionAsync(client);
        var secondResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055401", "6 B", null, "Operador", 4, null));
        var second = await secondResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(second);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/sessions/{second!.Id}/status", new UpdateSessionStatusRequest("closed"))).StatusCode);

        factory.SeedAttempt(open.Id, "active-attempt", "student-a", 501, "in_progress", "2026-10-01T10:10:00Z");
        var active = await client.GetFromJsonAsync<JsonElement>("/api/sessions/active?schoolCode=1800-55400");
        var activeItem = Assert.Single(active.EnumerateArray());
        Assert.Equal("Escuela Norte", activeItem.GetProperty("schoolName").GetString());
        Assert.Equal("Matematica 6", activeItem.GetProperty("examTitle").GetString());
        Assert.Equal("6° A · Turno mañana", activeItem.GetProperty("gradeLabel").GetString());
        Assert.Equal("snapshot-a", activeItem.GetProperty("rosterSnapshotId").GetString());
        Assert.Equal("section-a", activeItem.GetProperty("rosterSectionId").GetString());
        Assert.Equal(0, activeItem.GetProperty("submittedCount").GetInt32());
        Assert.Equal(1, activeItem.GetProperty("inProgressCount").GetInt32());
        var progress = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{open.Id}/progress");
        Assert.Equal("180055400", progress.GetProperty("schoolCode").GetString());
        Assert.Equal("Escuela Norte", progress.GetProperty("schoolName").GetString());
        Assert.Equal("6° A · Turno mañana", progress.GetProperty("gradeLabelWithShift").GetString());

        var page = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?page=1&pageSize=1");
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Single(page.GetProperty("items").EnumerateArray());
        var beyondEnd = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?page=3&pageSize=1");
        Assert.Equal(2, beyondEnd.GetProperty("totalCount").GetInt32());
        Assert.Empty(beyondEnd.GetProperty("items").EnumerateArray());
        var filtered = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?schoolCode=180055400&status=active");
        Assert.Equal(1, filtered.GetProperty("totalCount").GetInt32());
        Assert.Equal(open.Id, filtered.GetProperty("items")[0].GetProperty("id").GetString());
        var gradeSectionFiltered = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?course=6%C2%BA&division=A&pageSize=1");
        Assert.Equal(1, gradeSectionFiltered.GetProperty("totalCount").GetInt32());
        Assert.Equal(open.Id, gradeSectionFiltered.GetProperty("items")[0].GetProperty("id").GetString());
        var gradeOptions = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history/filters");
        Assert.Contains(gradeOptions.EnumerateArray(), option => option.GetProperty("course").GetString() == "6º"
            && option.GetProperty("division").GetString() == "A");

        factory.SetSchoolName("180055400", "Escuela N° 123 \"Dr. Juan PÚJOL\"");
        factory.SetSchoolName("180055401", "Escuela N° 123 \"Dr. Juan PÚJOL\"");
        factory.SetExamTitle("MATEMÁTICA DIAGNÓSTICO ÓSCAR 6");
        factory.SetSectionShift("section-a", "TARDE");
        factory.SetAccessCode(open.Id, "K7M-4PQ");
        foreach (var query in new[] { "pujol", "PUJOL", "Pújol", "dr juan", "juan pujol 123", "matematica 6", "matemática diagnóstico", "oscar" })
        {
            var searched = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/history?q={Uri.EscapeDataString(query)}");
            Assert.Equal(2, searched.GetProperty("totalCount").GetInt32());
            Assert.Contains(searched.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == open.Id);
        }
        foreach (var query in new[] { "6a", "6 a", "6° A", "6to a", "sexto a" })
        {
            var searched = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/history?q={Uri.EscapeDataString(query)}");
            Assert.Equal(2, searched.GetProperty("totalCount").GetInt32());
            Assert.Contains(searched.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == open.Id);
        }
        var exactCue = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=180055400");
        Assert.Equal(1, exactCue.GetProperty("totalCount").GetInt32());
        Assert.Equal(open.Id, exactCue.GetProperty("items")[0].GetProperty("id").GetString());
        var partialCue = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=1800554");
        Assert.Equal(2, partialCue.GetProperty("totalCount").GetInt32());
        foreach (var query in new[] { "K7M-4PQ", "k7m4pq", "4pq", "6 a tarde", "tarde" })
        {
            var searched = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/history?q={Uri.EscapeDataString(query)}");
            Assert.Equal(1, searched.GetProperty("totalCount").GetInt32());
            Assert.Equal(open.Id, searched.GetProperty("items")[0].GetProperty("id").GetString());
        }
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=notaword")).GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=a")).GetProperty("totalCount").GetInt32());
        var closedSearch = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=cerrada%20pujol");
        Assert.Equal(1, closedSearch.GetProperty("totalCount").GetInt32());
        Assert.Equal(second!.Id, closedSearch.GetProperty("items")[0].GetProperty("id").GetString());
        factory.SetSectionShift("section-a", "ÑANDÚ");
        var uppercaseShiftSearch = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=nandu");
        Assert.Equal(1, uppercaseShiftSearch.GetProperty("totalCount").GetInt32());
        Assert.Equal(open.Id, uppercaseShiftSearch.GetProperty("items")[0].GetProperty("id").GetString());
        factory.SetSectionShift("section-a", "MAÑANA");
        foreach (var query in new[] { "manana", "mañana" })
        {
            var shiftSearch = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/history?q={Uri.EscapeDataString(query)}");
            Assert.Equal(1, shiftSearch.GetProperty("totalCount").GetInt32());
            Assert.Equal(open.Id, shiftSearch.GetProperty("items")[0].GetProperty("id").GetString());
        }
        factory.SetSectionShift("section-a", "NOCHE");
        var nightSearch = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=noche");
        Assert.Equal(1, nightSearch.GetProperty("totalCount").GetInt32());

        var schoolsResponse = await client.GetAsync("/api/schools");
        Assert.True(schoolsResponse.IsSuccessStatusCode, await schoolsResponse.Content.ReadAsStringAsync());
        var schools = (await schoolsResponse.Content.ReadFromJsonAsync<JsonElement>()).Clone();
        Assert.Contains(schools.EnumerateArray(), school => school.GetProperty("code").GetString() == "180055400" && school.GetProperty("hasReadyRoster").GetBoolean());
    }

    [Fact]
    public async Task History_filters_same_division_by_shift_and_keeps_shift_in_grade_label()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-morning", "section-morning", "Ready");
        factory.SeedRosterStudent("snapshot-morning", "section-morning", "student-morning", 751, "75.000.001", "Ana", "Mañana");
        var morning = await CreateRosterSessionForSectionAsync(client, "180055400", "snapshot-morning", "section-morning");
        factory.SeedRoster("180055400", "2026", "snapshot-afternoon", "section-afternoon", "Ready");
        factory.SeedRosterStudent("snapshot-afternoon", "section-afternoon", "student-afternoon", 752, "75.000.002", "Ana", "Tarde");
        factory.SetSectionShift("section-afternoon", "Tarde");
        var afternoon = await CreateRosterSessionForSectionAsync(client, "180055400", "snapshot-afternoon", "section-afternoon");

        var morningPage = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?course=6%C2%BA&division=A&shift=Ma%C3%B1ana&pageSize=1");
        Assert.Equal(1, morningPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(morning.Id, morningPage.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal("6° A · Turno mañana", morningPage.GetProperty("items")[0].GetProperty("gradeLabel").GetString());

        var afternoonPage = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?course=6%C2%BA&division=A&shift=Tarde&pageSize=1");
        Assert.Equal(1, afternoonPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(afternoon.Id, afternoonPage.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal("6° A · Turno tarde", afternoonPage.GetProperty("items")[0].GetProperty("gradeLabel").GetString());

        var options = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history/filters");
        Assert.Contains(options.EnumerateArray(), item => item.GetProperty("division").GetString() == "A" && item.GetProperty("shift").GetString() == "Mañana");
        Assert.Contains(options.EnumerateArray(), item => item.GetProperty("division").GetString() == "A" && item.GetProperty("shift").GetString() == "Tarde");
    }

    [Fact]
    public async Task History_search_finds_multitoken_result_across_five_thousand_sessions_quickly()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var expectedId = factory.SeedSearchSessions(5000);

        var stopwatch = Stopwatch.StartNew();
        var page = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=pujol%206%20a&pageSize=20");
        stopwatch.Stop();

        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(expectedId, page.GetProperty("items")[0].GetProperty("id").GetString());
        var timing = $"5,000-session multi-token history search: {stopwatch.Elapsed.TotalMilliseconds:F1} ms";
        output.WriteLine(timing);
        Console.WriteLine(timing);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"History search took {stopwatch.Elapsed.TotalMilliseconds:F1} ms.");
    }

    [Fact]
    public async Task History_grade_search_does_not_match_school_or_cue_text_as_grade()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "grade-a-snapshot", "grade-a", "Ready");
        factory.SeedRosterStudent("grade-a-snapshot", "grade-a", "grade-a-student", 601, "60.000.001", "Ana", "Curso");
        factory.SetSchoolName("180055400", "Escuela Pújol");
        var sectionA = await CreateRosterSessionForSectionAsync(client, "180055400", "grade-a-snapshot", "grade-a");

        factory.SeedRoster("180055406", "2026", "grade-b-snapshot", "grade-b", "Ready");
        factory.SeedRosterStudent("grade-b-snapshot", "grade-b", "grade-b-student", 602, "60.000.002", "Bea", "Curso");
        factory.SetSectionDivision("grade-b", "B");
        factory.SetSchoolName("180055406", "Escuela Alfa");
        var sectionB = await CreateRosterSessionForSectionAsync(client, "180055406", "grade-b-snapshot", "grade-b");

        factory.SeedRoster("180055407", "2026", "grade-16-snapshot", "grade-16", "Ready");
        factory.SeedRosterStudent("grade-16-snapshot", "grade-16", "grade-16-student", 603, "60.000.003", "Cora", "Curso");
        factory.SetSectionCourse("grade-16", "16º");
        factory.SetSchoolName("180055407", "Escuela Alfa 16");
        var section16 = await CreateRosterSessionForSectionAsync(client, "180055407", "grade-16-snapshot", "grade-16");

        var ordinalSessions = new List<(string Query, LocalDeliverySession Session)>();
        foreach (var (cue, snapshot, section, course, query) in new[]
        {
            ("180055408", "grade-1-snapshot", "grade-1", "1°", "1ro"),
            ("180055409", "grade-3-snapshot", "grade-3", "3°", "3er"),
            ("180055410", "grade-7-snapshot", "grade-7", "7°", "7mo")
        })
        {
            factory.SeedRoster(cue, "2026", snapshot, section, "Ready");
            factory.SeedRosterStudent(snapshot, section, $"{section}-student", 700 + ordinalSessions.Count, $"70.000.00{ordinalSessions.Count}", "Alumno", "Curso");
            factory.SetSectionCourse(section, course);
            ordinalSessions.Add((query, await CreateRosterSessionForSectionAsync(client, cue, snapshot, section)));
        }

        foreach (var query in new[] { "6a", "6 a", "6° A", "6to a", "sexto a" })
        {
            var page = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/history?q={Uri.EscapeDataString(query)}");
            Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(sectionA.Id, page.GetProperty("items")[0].GetProperty("id").GetString());
        }
        var compoundWithSchoolName = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=6a%20pujol");
        Assert.Equal(sectionA.Id, compoundWithSchoolName.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(1, compoundWithSchoolName.GetProperty("totalCount").GetInt32());
        var cueDoesNotImplyGrade = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=6%20a");
        Assert.DoesNotContain(cueDoesNotImplyGrade.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == sectionB.Id);
        var grade16 = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=16a");
        Assert.Equal(section16.Id, grade16.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.DoesNotContain(grade16.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == sectionA.Id);
        var grade6b = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history?q=6b");
        Assert.Equal(1, grade6b.GetProperty("totalCount").GetInt32());
        Assert.Equal(sectionB.Id, grade6b.GetProperty("items")[0].GetProperty("id").GetString());
        foreach (var (query, session) in ordinalSessions)
        {
            var page = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/history?q={Uri.EscapeDataString(query)}");
            Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
            Assert.Equal(session.Id, page.GetProperty("items")[0].GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task History_school_and_status_filters_use_the_compound_history_index()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var queryPlan = factory.ExplainSchoolAndStatusHistoryPlan("180055400", "active");

        Assert.Contains("SEARCH s USING INDEX ix_delivery_sessions_school_status_start", queryPlan, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Closed_session_progress_remains_available_for_read_only_summary()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var session = await CreateSessionAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("closed"))).StatusCode);
        var response = await client.GetAsync($"/api/sessions/{session.Id}/progress");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var progress = await response.Content.ReadFromJsonAsync<LocalSessionProgress>();
        Assert.NotNull(progress);
        Assert.Equal(session.Id, progress!.SessionId);
    }

    [Fact]
    public async Task Progress_returns_roster_students_with_mixed_statuses_and_grade_label()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "student-a", 501, "12.345.678", "Ana", "Pérez");
        factory.SeedRosterStudent("snapshot-a", "section-a", "student-b", 502, "23.456.789", "Luis", "Gómez");
        factory.SeedRosterStudent("snapshot-a", "section-a", "student-c", 503, "34.567.890", "Marta", "Rodríguez");
        var session = await CreateRosterSessionAsync(client);
        factory.SeedAttempt(session.Id, "attempt-a", "student-a", 501, "in_progress", "2026-10-01T10:00:00Z");
        factory.SeedAttempt(session.Id, "attempt-b", "student-b", 502, "submitted", "2026-10-01T10:01:00Z", "2026-10-01T10:20:00Z");
        factory.SeedAttempt(session.Id, "attempt-off-roster", null, 999, "submitted", "2026-10-01T10:02:00Z", "2026-10-01T10:21:00Z", "No", "Incluido");

        var progress = await client.GetFromJsonAsync<LocalSessionProgress>($"/api/sessions/{session.AccessCode}/progress");

        Assert.NotNull(progress);
        Assert.True(progress!.HasRoster);
        Assert.Equal(3, progress.StartedCount);
        Assert.Equal(2, progress.SubmittedCount);
        Assert.Equal(1, progress.InProgressCount);
        Assert.Equal("6° A · Turno mañana", progress.GradeLabel);
        Assert.Equal("6º", progress.Course);
        Assert.Equal("A", progress.Division);
        Assert.Equal("Mañana", progress.Shift);
        Assert.Equal("Primario", progress.Level);
        Assert.Collection(progress.Students,
            student => { Assert.Equal("student-b", student.Id); Assert.Equal("Gómez, Luis", student.DisplayName); Assert.Equal("submitted", student.Status); Assert.Equal("**.***.5678", student.MaskedDocument); },
            student => { Assert.Equal("attempt-off-roster", student.Id); Assert.Equal("Incluido, No", student.DisplayName); Assert.Equal("submitted", student.Status); },
            student => { Assert.Equal("student-a", student.Id); Assert.Equal("Pérez, Ana", student.DisplayName); Assert.Equal("in_progress", student.Status); },
            student => { Assert.Equal("student-c", student.Id); Assert.Equal("Rodríguez, Marta", student.DisplayName); Assert.Equal("not_started", student.Status); Assert.Null(student.AttemptId); });
        Assert.All(progress.Students, student => { Assert.Null(student.SubmissionReason); Assert.False(student.OffRoster); });
    }

    [Fact]
    public async Task Roster_completion_excludes_teacher_added_submissions_and_reports_them_separately()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        for (var index = 0; index < 20; index++)
        {
            var studentId = $"student-{index:D2}";
            factory.SeedRosterStudent("snapshot-a", "section-a", studentId, 500 + index,
                $"1000000{index:D2}", $"Nombre{index:D2}", $"Apellido{index:D2}");
        }

        var session = await CreateRosterSessionAsync(client, expectedStudentCount: 20);
        for (var index = 0; index < 19; index++)
        {
            factory.SeedAttempt(session.Id, $"attempt-{index:D2}", $"student-{index:D2}", 500 + index,
                "submitted", "2026-10-01T10:00:00Z", "2026-10-01T10:20:00Z");
        }

        var extraResponse = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/extra-students",
            new AddSessionExtraStudentRequest("98.765.432", "Bruno", "Díaz"));
        Assert.Equal(HttpStatusCode.Created, extraResponse.StatusCode);
        var extraId = (await extraResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        factory.SeedAttempt(session.Id, "attempt-extra", null, 999, "submitted", "2026-10-01T10:02:00Z",
            "2026-10-01T10:21:00Z", "Bruno", "Díaz", extraId);

        var progress = await client.GetFromJsonAsync<LocalSessionProgress>($"/api/sessions/{session.AccessCode}/progress");
        Assert.NotNull(progress);
        Assert.Equal(20, progress!.ExpectedStudentCount);
        Assert.Equal(19, progress.StartedCount);
        Assert.Equal(19, progress.SubmittedCount);
        Assert.Equal(95, progress.CompletionPercentage);
        Assert.Equal(1, progress.OffRosterSubmittedCount);

        var active = await client.GetFromJsonAsync<JsonElement>("/api/sessions/active");
        var activeItem = Assert.Single(active.EnumerateArray());
        Assert.Equal(19, activeItem.GetProperty("submittedCount").GetInt32());
        Assert.Equal(1, activeItem.GetProperty("offRosterSubmittedCount").GetInt32());
        Assert.Equal(0, activeItem.GetProperty("offRosterInProgressCount").GetInt32());

        var history = await client.GetFromJsonAsync<JsonElement>("/api/sessions/history");
        var historyItem = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal(19, historyItem.GetProperty("submittedCount").GetInt32());
        Assert.Equal(1, historyItem.GetProperty("offRosterSubmittedCount").GetInt32());
    }

    [Fact]
    public async Task Progress_sorts_accented_surnames_using_argentine_spanish_collation()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "student-z", 510, "12.345.670", "Zoe", "Zapata");
        factory.SeedRosterStudent("snapshot-a", "section-a", "student-a", 511, "12.345.671", "Alba", "Álvarez");
        factory.SeedRosterStudent("snapshot-a", "section-a", "student-n", 512, "12.345.672", "Nora", "Ñandú");
        factory.SeedRosterStudent("snapshot-a", "section-a", "student-o", 513, "12.345.673", "Olga", "Órdenes");
        var session = await CreateRosterSessionAsync(client);

        var progress = await client.GetFromJsonAsync<LocalSessionProgress>($"/api/sessions/{session.AccessCode}/progress");

        Assert.NotNull(progress);
        Assert.Equal(new[] { "Álvarez, Alba", "Ñandú, Nora", "Órdenes, Olga", "Zapata, Zoe" },
            progress!.Students.Select(student => student.DisplayName));
    }

    [Fact]
    public async Task Progress_non_nominal_lists_attempts_only_and_uses_exam_grade_metadata()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var progress = await client.GetFromJsonAsync<LocalSessionProgress>($"/api/sessions/{session.AccessCode}/progress");

        Assert.NotNull(progress);
        Assert.Equal("6° A", progress!.GradeLabel);
        Assert.Equal("6", progress.Course);
        Assert.Equal("A", progress.Division);
        Assert.Null(progress.Shift);
        var student = Assert.Single(progress.Students);
        Assert.Equal(started.Attempt.Id, student.Id);
        Assert.Equal(started.Attempt.Id, student.AttemptId);
        Assert.Equal("in_progress", student.Status);
        Assert.Null(student.MaskedDocument);
    }

    [Fact]
    public async Task Progress_nominal_with_empty_roster_returns_empty_student_list_and_section_grade()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "temporary-student", 600, "56.789.012", "Eva", "Prueba");
        factory.SetRosterSection("section-a", "4", "B", "Tarde", "Primario");
        var session = await CreateRosterSessionAsync(client);
        factory.DeleteRosterStudents("section-a");

        var progress = await client.GetFromJsonAsync<LocalSessionProgress>($"/api/sessions/{session.AccessCode}/progress");

        Assert.NotNull(progress);
        Assert.Empty(progress!.Students);
        Assert.Equal("4° B · Turno tarde", progress.GradeLabel);
        Assert.Equal("4", progress.Course);
        Assert.Equal("Tarde", progress.Shift);
    }

    [Theory]
    [InlineData("6", "A", "Mañana", "6° A · Turno mañana")]
    [InlineData("6º", " A ", " TARDE ", "6° A · Turno tarde")]
    [InlineData("Primario", null, null, "Primario")]
    [InlineData(null, "B", null, "B")]
    [InlineData(null, null, "Noche", "Turno noche")]
    [InlineData(null, null, null, null)]
    public void Grade_label_formatter_formats_available_parts(string? course, string? division, string? shift, string? expected)
    {
        Assert.Equal(expected, PlanCope.Local.Api.Services.GradeLabelFormatter.Format(course, division, shift));
    }

    [Fact]
    public async Task Session_attempt_submit_flow_writes_pending_outbox_item()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var session = await CreateSessionAsync(client);

        var progress = await client.GetFromJsonAsync<LocalSessionProgress>($"/api/sessions/{session.AccessCode}/progress");
        Assert.NotNull(progress);
        Assert.Equal(0, progress.StartedCount);

        var startResponse = await client.PostAsync($"/api/sessions/{session.AccessCode}/attempts", null);
        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);

        var started = await startResponse.Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        SetResumeCredential(client, started!);
        Assert.NotNull(started.Attempt);
        Assert.NotEmpty(started.Blocks);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            revision = 1,
            answers = new[]
            {
                new { blockId = factory.QuestionBlockId, answer = "42" }
            }
        });
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var confirmation = await submitResponse.Content.ReadFromJsonAsync<SubmitAttemptResponse>();
        Assert.NotNull(confirmation);
        Assert.False(string.IsNullOrWhiteSpace(confirmation.ConfirmationCode));

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_type, aggregate_type, aggregate_id, payload_json, status
            FROM sync_outbox
            LIMIT 1;
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(SyncEventTypes.AttemptSubmitted, reader.GetString(0));
        var declaredEventTypes = typeof(SyncEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.GetValue(null)).OfType<string>().ToHashSet(StringComparer.Ordinal);
        Assert.Contains(reader.GetString(0), declaredEventTypes);
        Assert.Equal("student_attempt", reader.GetString(1));
        Assert.Equal(started.Attempt.Id, reader.GetString(2));
        Assert.Equal("pending", reader.GetString(4));

        using var payload = JsonDocument.Parse(reader.GetString(3));
        Assert.Equal("submitted", payload.RootElement.GetProperty("attempt").GetProperty("status").GetString());
        Assert.Single(payload.RootElement.GetProperty("answers").EnumerateArray());
        var deliverySession = payload.RootElement.GetProperty("deliverySession");
        Assert.Equal(session.Id, deliverySession.GetProperty("id").GetString());
        Assert.Equal("180055400", deliverySession.GetProperty("schoolCue").GetString());
        Assert.Equal("remote-test-matematica-6-v1", deliverySession.GetProperty("examVersionId").GetString());
        Assert.True(deliverySession.TryGetProperty("sectionId", out _));
    }

    [Fact]
    public async Task Attempt_restore_requires_credential_and_reconciles_answer_revisions_idempotently()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);
        var retriedStart = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResumeCredential: started.ResumeCredential));
        Assert.Equal(HttpStatusCode.Created, retriedStart.StatusCode);
        Assert.Equal(started.Attempt.Id, (await retriedStart.Content.ReadFromJsonAsync<StartAttemptResponse>())!.Attempt.Id);

        using var unauthenticated = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.GetAsync($"/api/attempts/{started.Attempt.Id}/restore")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            revision = 1,
            answers = new[] { new { blockId = factory.QuestionBlockId, answer = "attempt-id-is-not-auth" } }
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SaveAnswersAsync(client, started.Attempt.Id, factory.QuestionBlockId, "first", revision: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SaveAnswersAsync(client, started.Attempt.Id, factory.QuestionBlockId, "newest", revision: 3)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SaveAnswersAsync(client, started.Attempt.Id, factory.QuestionBlockId, "out-of-order", revision: 2)).StatusCode);

        var restored = await client.GetFromJsonAsync<JsonElement>($"/api/attempts/{started.Attempt.Id}/restore");
        Assert.Equal(started.Attempt.Id, restored.GetProperty("attempt").GetProperty("id").GetString());
        var answer = Assert.Single(restored.GetProperty("answers").EnumerateArray());
        Assert.Equal("newest", answer.GetProperty("answer").GetString());
        Assert.Equal(3L, answer.GetProperty("revision").GetInt64());

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM student_attempts WHERE id = $id;";
        command.Parameters.AddWithValue("$id", started.Attempt.Id);
        Assert.Equal(1L, (long)command.ExecuteScalar()!);

        command.CommandText = "UPDATE attempt_resume_credentials SET expires_at = '2000-01-01T00:00:00.0000000+00:00' WHERE attempt_id = $id;";
        command.ExecuteNonQuery();
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.GetAsync($"/api/attempts/{started.Attempt.Id}/restore")).StatusCode);
    }

    [Fact]
    public async Task Resume_credential_revocation_is_idempotent_expiry_safe_and_token_specific()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);
        var revokePath = $"/api/attempts/{started.Attempt.Id}/resume-credential/revoke";

        using var unauthenticated = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.PostAsync(revokePath, null)).StatusCode);

        var rotatedCredential = "rotated-resume-credential-with-at-least-32-chars";
        using (var connection = factory.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE attempt_resume_credentials SET token_hash = $hash, expires_at = $expires WHERE attempt_id = $id;";
            command.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rotatedCredential))).ToLowerInvariant());
            command.Parameters.AddWithValue("$expires", DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
            command.Parameters.AddWithValue("$id", started.Attempt.Id);
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        SetResumeCredential(client, started);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(revokePath, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(revokePath, null)).StatusCode);

        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {rotatedCredential}");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/attempts/{started.Attempt.Id}/restore")).StatusCode);

        using (var connection = factory.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE attempt_resume_credentials SET expires_at = '2000-01-01T00:00:00.0000000+00:00' WHERE attempt_id = $id;";
            command.Parameters.AddWithValue("$id", started.Attempt.Id);
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(revokePath, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(revokePath, null)).StatusCode);

        var revokeBySession = $"/api/sessions/{session.AccessCode}/attempts/resume-credential/revoke";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(revokeBySession, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(revokeBySession, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/sessions/{session.AccessCode}/attempts",
            new StartAttemptRequest(ResumeCredential: rotatedCredential))).StatusCode);
    }

    [Fact]
    public async Task Session_revocation_cleans_up_attempt_when_start_response_was_lost()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var session = await CreateSessionAsync(client);
        var lostResponseCredential = "lost-start-resume-credential-with-more-than-32-characters";
        var start = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResumeCredential: lostResponseCredential));
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {lostResponseCredential}");

        using var unauthenticated = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthenticated.PostAsync(
            $"/api/sessions/{session.AccessCode}/attempts/resume-credential/revoke", null)).StatusCode);
        var revokePath = $"/api/sessions/{session.AccessCode}/attempts/resume-credential/revoke";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(revokePath, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(revokePath, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/sessions/{session.AccessCode}/attempts",
            new StartAttemptRequest(ResumeCredential: lostResponseCredential))).StatusCode);
    }

    [Fact]
    public async Task Concurrent_answer_save_and_submit_cannot_write_after_delivery()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var save = SaveAnswersAsync(client, started.Attempt.Id, factory.QuestionBlockId, "last edit", revision: 1);
        var submit = client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        await Task.WhenAll(save, submit);

        Assert.Equal(HttpStatusCode.OK, submit.Result.StatusCode);
        Assert.Contains(save.Result.StatusCode, new[] { HttpStatusCode.NoContent, HttpStatusCode.Unauthorized });
        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, (SELECT COUNT(*) FROM attempt_resume_credentials WHERE attempt_id = student_attempts.id), (SELECT COUNT(*) FROM submission_answers WHERE student_attempt_id = student_attempts.id) FROM student_attempts WHERE id = $id;";
        command.Parameters.AddWithValue("$id", started.Attempt.Id);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("submitted", reader.GetString(0));
        Assert.Equal(0L, reader.GetInt64(1));
        Assert.Equal(save.Result.StatusCode == HttpStatusCode.NoContent ? 1L : 0L, reader.GetInt64(2));
    }

    [Fact]
    public async Task Starting_attempt_for_missing_or_paused_session_returns_error()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var missingResponse = await client.PostAsync("/api/sessions/NOPE1/attempts", null);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        var session = await CreateSessionAsync(client);
        var pauseResponse = await client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("paused"));
        Assert.Equal(HttpStatusCode.NoContent, pauseResponse.StatusCode);

        var pausedResponse = await client.PostAsync($"/api/sessions/{session.AccessCode}/attempts", null);
        Assert.Equal(HttpStatusCode.BadRequest, pausedResponse.StatusCode);
    }

    [Fact]
    public async Task Submitted_attempt_cannot_be_edited_or_submitted_again()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var session = await CreateSessionAsync(client);
        var started = await (await client.PostAsync($"/api/sessions/{session.AccessCode}/attempts", null))
            .Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        SetResumeCredential(client, started!);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var answerResponse = await client.PutAsJsonAsync($"/api/attempts/{started.Attempt.Id}/answers", new
        {
            revision = 1,
            answers = new[]
            {
                new { blockId = factory.QuestionBlockId, answer = "44" }
            }
        });
        Assert.Equal(HttpStatusCode.NotFound, answerResponse.StatusCode);

        var duplicateSubmitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.NotFound, duplicateSubmitResponse.StatusCode);

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sync_outbox WHERE aggregate_id = $attemptId;";
        command.Parameters.AddWithValue("$attemptId", started.Attempt.Id);
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public async Task Saving_answers_and_submitting_on_active_session_succeeds()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var answerResponse = await SaveAnswersAsync(client, started.Attempt.Id, factory.QuestionBlockId, "42");
        Assert.Equal(HttpStatusCode.NoContent, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
    }

    [Fact]
    public async Task Saving_answers_and_submitting_on_paused_session_are_rejected()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var pauseResponse = await client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("paused"));
        Assert.Equal(HttpStatusCode.NoContent, pauseResponse.StatusCode);

        var answerResponse = await SaveAnswersAsync(client, started.Attempt.Id, factory.QuestionBlockId, "42");
        Assert.Equal(HttpStatusCode.BadRequest, answerResponse.StatusCode);
        var answerBody = await answerResponse.Content.ReadAsStringAsync();
        Assert.Contains("pausada", answerBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cerrada", answerBody, StringComparison.OrdinalIgnoreCase);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, submitResponse.StatusCode);
        var submitBody = await submitResponse.Content.ReadAsStringAsync();
        Assert.Contains("pausada", submitBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cerrada", submitBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Closing_session_submits_attempt_and_rejects_later_changes()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var closeResponse = await client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("closed"));
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        using var closeSummary = JsonDocument.Parse(await closeResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, closeSummary.RootElement.GetProperty("submitted").GetInt32());
        Assert.Equal(0, closeSummary.RootElement.GetProperty("failed").GetInt32());

        var answerResponse = await SaveAnswersAsync(client, started.Attempt.Id, factory.QuestionBlockId, "42");
        Assert.Equal(HttpStatusCode.NotFound, answerResponse.StatusCode);

        var submitResponse = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.NotFound, submitResponse.StatusCode);
    }

    [Fact]
    public async Task Resuming_paused_session_reenables_saving_and_submitting_on_the_same_attempt()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        var session = await CreateSessionAsync(client);
        var started = await StartAttemptAsync(client, session.AccessCode);

        var pauseResponse = await client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("paused"));
        Assert.Equal(HttpStatusCode.NoContent, pauseResponse.StatusCode);

        var pausedSave = await SaveAnswersAsync(client, started.Attempt.Id, factory.QuestionBlockId, "42");
        Assert.Equal(HttpStatusCode.BadRequest, pausedSave.StatusCode);
        Assert.Contains("pausada", await pausedSave.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var pausedSubmit = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, pausedSubmit.StatusCode);
        Assert.Contains("pausada", await pausedSubmit.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var resumeResponse = await client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("active"));
        Assert.Equal(HttpStatusCode.NoContent, resumeResponse.StatusCode);

        var resumedSave = await SaveAnswersAsync(client, started.Attempt.Id, factory.QuestionBlockId, "42");
        Assert.Equal(HttpStatusCode.NoContent, resumedSave.StatusCode);

        var resumedSubmit = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, resumedSubmit.StatusCode);
    }

    [Fact]
    public async Task Invalid_session_request_returns_validation_problem()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest("", "", null, null, "", 0, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("errors", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Pending_expiry_blocks_starting_new_sessions()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        using (var connection = factory.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO sync_state (id, key, value_json, updated_at) VALUES ('expiry-pending', 'activation_expiry_pending', 'true', @Now);";
            command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055400", null, null, "Operador", 1, null));

        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
        Assert.Contains("Finalizá", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nominal_session_links_to_a_ready_roster_snapshot_and_section()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a", 501, "12.345.678", "Ana", "Pérez");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-b", 502, "23.456.789", "Luis", "Gómez");

        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055400", "6 A", null, "Operador", 30, null,
            "2026", "snapshot-a", "section-a"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        Assert.Equal("2026", session!.SchoolYear);
        Assert.Equal("snapshot-a", session.RosterSnapshotId);
        Assert.Equal("section-a", session.RosterSectionId);
        Assert.Equal(2, session.ExpectedStudentCount);
    }

    [Fact]
    public async Task Nominal_session_rejects_a_section_from_another_snapshot()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRoster("180055400", "2026", "snapshot-b", "section-b", "Ready");

        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055400", "6 B", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-b"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no pertenece", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nominal_session_rejects_a_snapshot_for_another_cue_or_year()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");

        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055401", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no corresponde", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nominal_resolution_requires_confirmation_and_stores_only_identity_snapshot()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a", 501, "12.345.678", "Ana", "Pérez");

        var session = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055400", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));
        var createdSession = await session.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(createdSession);

        var resolutionResponse = await client.PostAsJsonAsync($"/api/sessions/{createdSession!.AccessCode}/student-resolution", new ResolveStudentRequest("12.345.678"));
        Assert.Equal(HttpStatusCode.OK, resolutionResponse.StatusCode);
        var resolution = await resolutionResponse.Content.ReadFromJsonAsync<ResolveStudentResponse>();
        Assert.NotNull(resolution);
        Assert.Equal("**.***.5678", resolution!.Student.MaskedDocument);
        Assert.DoesNotContain("12345678", await resolutionResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var startResponse = await client.PostAsJsonAsync($"/api/sessions/{createdSession.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution.ResolutionToken));
        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);
        var started = await startResponse.Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        SetResumeCredential(client, started!);
        Assert.Equal("GE:501", started!.Attempt.StudentCode);

        var repeatedStart = await client.PostAsJsonAsync($"/api/sessions/{createdSession.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution.ResolutionToken, ResumeCredential: started.ResumeCredential));
        Assert.Equal(HttpStatusCode.Created, repeatedStart.StatusCode);
        Assert.Equal(started.Attempt.Id, (await repeatedStart.Content.ReadFromJsonAsync<StartAttemptResponse>())!.Attempt.Id);
        using var restore = await client.GetAsync($"/api/attempts/{started.Attempt.Id}/restore");
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Contains("GE:501", await restore.Content.ReadAsStringAsync());

        using (var expireConnection = factory.CreateConnection())
        using (var expireCommand = expireConnection.CreateCommand())
        {
            expireCommand.CommandText = "UPDATE attempt_resume_credentials SET expires_at = '2000-01-01T00:00:00.0000000+00:00' WHERE attempt_id = $id;";
            expireCommand.Parameters.AddWithValue("$id", started.Attempt.Id);
            expireCommand.ExecuteNonQuery();
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/attempts/{started.Attempt.Id}/restore")).StatusCode);
        var renewedResolution = await client.PostAsJsonAsync($"/api/sessions/{createdSession.AccessCode}/student-resolution", new ResolveStudentRequest("12.345.678"));
        var renewedToken = (await renewedResolution.Content.ReadFromJsonAsync<ResolveStudentResponse>())!.ResolutionToken;
        var reentered = await client.PostAsJsonAsync($"/api/sessions/{createdSession.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: renewedToken));
        Assert.Equal(HttpStatusCode.Created, reentered.StatusCode);
        var renewedAttempt = (await reentered.Content.ReadFromJsonAsync<StartAttemptResponse>())!;
        Assert.Equal(started.Attempt.Id, renewedAttempt.Attempt.Id);
        SetResumeCredential(client, renewedAttempt);

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT student_code, ge_person_id, student_first_name, student_last_name, document_last4, verification_source, verified_at FROM student_attempts;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("GE:501", reader.GetString(0));
        Assert.Equal(501, reader.GetInt32(1));
        Assert.Equal("5678", reader.GetString(4));
        Assert.Equal("ge_roster", reader.GetString(5));
    }

    [Fact]
    public async Task Teacher_added_off_roster_student_can_resolve_start_and_sync_without_plain_document()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a", 501, "12.345.678", "Ana", "Pérez");
        var session = await CreateRosterSessionAsync(client);

        var rosterDni = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/extra-students",
            new AddSessionExtraStudentRequest("12.345.678", "Otra", "Persona"));
        Assert.Equal(HttpStatusCode.Conflict, rosterDni.StatusCode);
        Assert.Contains("Ese DNI ya figura en el padrón de la sección.", await rosterDni.Content.ReadAsStringAsync());

        var addedResponse = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/extra-students",
            new AddSessionExtraStudentRequest("98.765.432", "  Bruno ", "  Díaz  "));
        Assert.Equal(HttpStatusCode.Created, addedResponse.StatusCode);
        var added = await addedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var extraId = added.GetProperty("id").GetString()!;
        Assert.Equal("Bruno", added.GetProperty("firstName").GetString());
        Assert.Equal("Díaz", added.GetProperty("lastName").GetString());
        Assert.True(added.GetProperty("offRoster").GetBoolean());
        Assert.DoesNotContain("98765432", await addedResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var duplicateExtra = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/extra-students",
            new AddSessionExtraStudentRequest("98765432", "Otro", "Nombre"));
        Assert.Equal(HttpStatusCode.Conflict, duplicateExtra.StatusCode);

        var removableResponse = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/extra-students",
            new AddSessionExtraStudentRequest("87.654.321", "Carla", "Rojas"));
        var removable = await removableResponse.Content.ReadFromJsonAsync<JsonElement>();
        var removableId = removable.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/sessions/{session.Id}/extra-students/{removableId}")).StatusCode);

        var unknown = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/student-resolution", new ResolveStudentRequest("87.654.321"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("¿Revisaste bien el DNI?", await unknown.Content.ReadAsStringAsync());

        var resolutionResponse = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/student-resolution", new ResolveStudentRequest("98.765.432"));
        Assert.Equal(HttpStatusCode.OK, resolutionResponse.StatusCode);
        var resolution = await resolutionResponse.Content.ReadFromJsonAsync<ResolveStudentResponse>();
        Assert.NotNull(resolution);
        Assert.Equal("Díaz, Bruno", resolution!.Student.DisplayName);
        Assert.Equal("**.***.5432", resolution.Student.MaskedDocument);

        var progress = await client.GetFromJsonAsync<LocalSessionProgress>($"/api/sessions/{session.AccessCode}/progress");
        var extraRow = Assert.Single(progress!.Students, student => student.Id == extraId);
        Assert.Equal("not_started", extraRow.Status);
        Assert.True(extraRow.OffRoster);

        var start = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution.ResolutionToken));
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var started = await start.Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        SetResumeCredential(client, started!);
        Assert.True(started!.Attempt.OffRoster);
        Assert.Null(started.Attempt.RosterStudentId);

        var secondResolution = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/student-resolution", new ResolveStudentRequest("98765432"));
        var secondToken = (await secondResolution.Content.ReadFromJsonAsync<ResolveStudentResponse>())!.ResolutionToken;
        var resumed = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: secondToken));
        Assert.Equal(HttpStatusCode.Created, resumed.StatusCode);
        var resumedAttempt = (await resumed.Content.ReadFromJsonAsync<StartAttemptResponse>())!;
        Assert.Equal(started.Attempt.Id, resumedAttempt.Attempt.Id);
        SetResumeCredential(client, resumedAttempt);

        var submit = await client.PostAsync($"/api/attempts/{started.Attempt.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_json FROM sync_outbox WHERE aggregate_id = $attemptId;";
        command.Parameters.AddWithValue("$attemptId", started.Attempt.Id);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        using var payload = JsonDocument.Parse(reader.GetString(0));
        var syncedAttempt = payload.RootElement.GetProperty("attempt");
        Assert.True(syncedAttempt.GetProperty("offRoster").GetBoolean());
        Assert.Equal("bruno", syncedAttempt.GetProperty("studentFirstName").GetString()?.ToLowerInvariant());
        Assert.Equal(64, syncedAttempt.GetProperty("documentHmac").GetString()?.Length);
        Assert.DoesNotContain("98765432", reader.GetString(0), StringComparison.Ordinal);

        var removeStarted = await client.DeleteAsync($"/api/sessions/{session.Id}/extra-students/{extraId}");
        Assert.Equal(HttpStatusCode.Conflict, removeStarted.StatusCode);
    }

    [Fact]
    public async Task Nominal_resolution_cannot_be_reused_or_started_twice()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a", 501, "12.345.678", "Ana", "Pérez");
        var sessionResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055400", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));
        var session = await sessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        var resolution = await (await client.PostAsJsonAsync($"/api/sessions/{session!.AccessCode}/student-resolution", new ResolveStudentRequest("12345678")))
            .Content.ReadFromJsonAsync<ResolveStudentResponse>();
        Assert.NotNull(resolution);

        var first = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution!.ResolutionToken));
        var second = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution.ResolutionToken));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Nominal_resolution_rejects_unknown_student_and_expired_token()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a", 501, "12.345.678", "Ana", "Pérez");
        var sessionResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055400", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));
        var session = await sessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);

        var unknown = await client.PostAsJsonAsync($"/api/sessions/{session!.AccessCode}/student-resolution", new ResolveStudentRequest("98.765.432"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var resolution = await (await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/student-resolution", new ResolveStudentRequest("12345678")))
            .Content.ReadFromJsonAsync<ResolveStudentResponse>();
        Assert.NotNull(resolution);
        factory.ExpireResolution(resolution!.ResolutionToken);
        var expired = await client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution.ResolutionToken));
        Assert.Equal(HttpStatusCode.Conflict, expired.StatusCode);
    }

    [Fact]
    public async Task Student_resolution_miss_leaks_no_student_name()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRoster("180055400", "2026", "snapshot-b", "section-b", "Ready");
        factory.SeedRoster("180055401", "2026", "snapshot-c", "section-c", "Ready");

        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a1", 501, "12.345.678", "Ana", "Pérez");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a2", 502, "23.456.789", "Luis", "Gómez");
        factory.SeedRosterStudent("snapshot-b", "section-b", "roster-student-b1", 503, "34.567.890", "Marta", "Rodríguez");
        factory.SeedRosterStudent("snapshot-c", "section-c", "roster-student-c1", 504, "45.678.901", "Carla", "Núñez");

        var sessionResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055400", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));
        var session = await sessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);

        string[] seededNames =
        {
            "Ana", "Pérez", "Luis", "Gómez", "Marta", "Rodríguez", "Carla", "Núñez"
        };

        string[] noMatchDocuments =
        {
            "98.765.432",
            "34.567.890",
            "45.678.901"
        };

        foreach (var document in noMatchDocuments)
        {
            var response = await client.PostAsJsonAsync(
                $"/api/sessions/{session!.AccessCode}/student-resolution",
                new ResolveStudentRequest(document));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();

            foreach (var name in seededNames)
            {
                Assert.DoesNotContain(name, body);
            }

            Assert.DoesNotContain("\"error\"", body);

            using var payload = JsonDocument.Parse(body);
            Assert.Equal("not_found", payload.RootElement.GetProperty("kind").GetString());
            Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("message").GetString()));
        }
    }

    [Fact]
    public async Task Nominal_resolution_concurrent_confirmation_creates_one_attempt()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        factory.SeedRoster("180055400", "2026", "snapshot-a", "section-a", "Ready");
        factory.SeedRosterStudent("snapshot-a", "section-a", "roster-student-a", 501, "12.345.678", "Ana", "Pérez");
        var sessionResponse = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055400", "6 A", null, "Operador", 2, null,
            "2026", "snapshot-a", "section-a"));
        var session = await sessionResponse.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        var resolution = await (await client.PostAsJsonAsync($"/api/sessions/{session!.AccessCode}/student-resolution", new ResolveStudentRequest("12345678")))
            .Content.ReadFromJsonAsync<ResolveStudentResponse>();
        Assert.NotNull(resolution);

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution!.ResolutionToken)),
            secondClient.PostAsJsonAsync($"/api/sessions/{session.AccessCode}/attempts", new StartAttemptRequest(ResolutionToken: resolution.ResolutionToken)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Closing_active_or_paused_session_submits_each_in_progress_attempt_once_with_reason()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var session = await CreateSessionAsync(client);
        var attempt = await StartAttemptAsync(client, session.AccessCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SaveAnswersAsync(client, attempt.Attempt.Id, factory.QuestionBlockId, "42")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("paused"))).StatusCode);

        var close = await client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("closed"));
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        using var result = JsonDocument.Parse(await close.Content.ReadAsStringAsync());
        Assert.Equal(1, result.RootElement.GetProperty("submitted").GetInt32());
        Assert.Equal(0, result.RootElement.GetProperty("failed").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("closed"))).StatusCode);

        using var connection = factory.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, submission_reason FROM student_attempts WHERE id = $id;";
        command.Parameters.AddWithValue("$id", attempt.Attempt.Id);
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("submitted", reader.GetString(0));
        Assert.Equal("closed_by_teacher", reader.GetString(1));
        reader.Close();
        command.CommandText = "SELECT payload_json FROM sync_outbox WHERE aggregate_id = $id;";
        using (var payload = JsonDocument.Parse((string)command.ExecuteScalar()!))
        {
            var syncedAnswers = payload.RootElement.GetProperty("answers").EnumerateArray().ToArray();
            Assert.Single(syncedAnswers);
            Assert.Equal("\"42\"", syncedAnswers[0].GetProperty("answerJson").GetString());
        }
        command.CommandText = "SELECT COUNT(*) FROM sync_outbox WHERE aggregate_id = $id;";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
        command.CommandText = "SELECT COUNT(*) FROM attempt_results WHERE student_attempt_id = $id;";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
        var progress = await client.GetFromJsonAsync<LocalSessionProgress>($"/api/sessions/{session.AccessCode}/progress");
        Assert.Equal("closed_by_teacher", Assert.Single(progress!.Students).SubmissionReason);
        Assert.Null(progress.AverageScorePercent);
    }

    [Fact]
    public async Task Concurrent_answer_save_and_teacher_close_preserve_every_confirmed_answer_in_durable_outbox()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();

        for (var iteration = 0; iteration < 8; iteration++)
        {
            var session = await CreateSessionAsync(client);
            var started = await StartAttemptAsync(client, session.AccessCode);

            var saveTask = SaveAnswersAsync(client, started.Attempt.Id, factory.QuestionBlockId, $"answer-{iteration}");
            var closeTask = client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("closed"));
            await Task.WhenAll(saveTask, closeTask);

            Assert.Equal(HttpStatusCode.OK, closeTask.Result.StatusCode);
            using var closeSummary = JsonDocument.Parse(await closeTask.Result.Content.ReadAsStringAsync());
            Assert.Equal(1, closeSummary.RootElement.GetProperty("submitted").GetInt32());
            Assert.Equal(0, closeSummary.RootElement.GetProperty("failed").GetInt32());
            Assert.True(
                saveTask.Result.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.NotFound,
                $"Unexpected answer save status: {(int)saveTask.Result.StatusCode}");

            using var connection = factory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT status FROM student_attempts WHERE id = $id;";
            command.Parameters.AddWithValue("$id", started.Attempt.Id);
            Assert.Equal("submitted", command.ExecuteScalar());

            command.CommandText = "SELECT payload_json FROM sync_outbox WHERE aggregate_id = $id;";
            using var payload = JsonDocument.Parse((string)command.ExecuteScalar()!);
            var durableAnswers = payload.RootElement.GetProperty("answers").EnumerateArray().ToArray();
            if (saveTask.Result.StatusCode == HttpStatusCode.NoContent)
            {
                Assert.Single(durableAnswers);
                Assert.Equal(JsonSerializer.Serialize($"answer-{iteration}"), durableAnswers[0].GetProperty("answerJson").GetString());
            }
            else
            {
                Assert.Empty(durableAnswers);
            }

            command.CommandText = "SELECT COUNT(*) FROM sync_outbox WHERE aggregate_id = $id;";
            Assert.Equal(1L, (long)command.ExecuteScalar()!);
        }
    }

    [Fact]
    public async Task Session_discard_is_allowed_only_before_first_attempt()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var empty = await CreateSessionAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/sessions/{empty.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/sessions/{empty.Id}")).StatusCode);

        var used = await CreateSessionAsync(client);
        await StartAttemptAsync(client, used.AccessCode);
        var response = await client.DeleteAsync($"/api/sessions/{used.Id}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("ya ingresaron alumnos", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Attempt_start_racing_with_close_is_submitted_or_rejected()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var session = await CreateSessionAsync(client);

        var startTask = client.PostAsync($"/api/sessions/{session.AccessCode}/attempts", null);
        var closeTask = client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("closed"));
        await Task.WhenAll(startTask, closeTask);

        var start = await startTask;
        var close = await closeTask;
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        using var summary = JsonDocument.Parse(await close.Content.ReadAsStringAsync());
        Assert.Equal(0, summary.RootElement.GetProperty("failed").GetInt32());

        if (start.StatusCode == HttpStatusCode.Created)
        {
            var started = await start.Content.ReadFromJsonAsync<StartAttemptResponse>();
            Assert.NotNull(started);
            SetResumeCredential(client, started!);
            using var connection = factory.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT status FROM student_attempts WHERE id = $id;";
            command.Parameters.AddWithValue("$id", started!.Attempt.Id);
            Assert.Equal("submitted", command.ExecuteScalar());
            Assert.Equal(1, summary.RootElement.GetProperty("submitted").GetInt32());
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, start.StatusCode);
            Assert.Equal(0, summary.RootElement.GetProperty("submitted").GetInt32());
        }
    }

    [Fact]
    public async Task Concurrent_close_requests_have_one_winner_and_no_false_failures()
    {
        using var factory = new LocalApiFactory();
        using var client = factory.CreateClient();
        await EnsureInitializedAsync(client);
        factory.SeedExam();
        var session = await CreateSessionAsync(client);
        await StartAttemptAsync(client, session.AccessCode);

        var closes = await Task.WhenAll(
            client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("closed")),
            client.PutAsJsonAsync($"/api/sessions/{session.Id}/status", new UpdateSessionStatusRequest("closed")));

        Assert.Single(closes, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(closes, response => response.StatusCode == HttpStatusCode.BadRequest);
        var successfulClose = closes.Single(response => response.StatusCode == HttpStatusCode.OK);
        using var summary = JsonDocument.Parse(await successfulClose.Content.ReadAsStringAsync());
        Assert.Equal(0, summary.RootElement.GetProperty("failed").GetInt32());
        Assert.Equal(1, summary.RootElement.GetProperty("submitted").GetInt32());
    }

    private static async Task<LocalDeliverySession> CreateSessionAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId,
            "180055400",
            "6A",
            null,
            "Operador",
            30,
            null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessCode));
        return session;
    }

    private static async Task<LocalDeliverySession> CreateRosterSessionAsync(HttpClient client, int expectedStudentCount = 2)
    {
        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, "180055400", "6 A", null, "Operador", expectedStudentCount, null,
            "2026", "snapshot-a", "section-a"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<LocalDeliverySession>();
        Assert.NotNull(session);
        return session!;
    }

    private static async Task<LocalDeliverySession> CreateRosterSessionForSectionAsync(HttpClient client, string cue, string snapshotId, string sectionId)
    {
        var response = await client.PostAsJsonAsync("/api/sessions/", new CreateSessionRequest(
            LocalApiFactory.ExamVersionId, cue, "Class", null, "Operador", 1, null, "2026", snapshotId, sectionId));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LocalDeliverySession>())!;
    }

    private static async Task<StartAttemptResponse> StartAttemptAsync(HttpClient client, string accessCode)
    {
        var response = await client.PostAsync($"/api/sessions/{accessCode}/attempts", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var started = await response.Content.ReadFromJsonAsync<StartAttemptResponse>();
        Assert.NotNull(started);
        SetResumeCredential(client, started!);
        return started;
    }

    private static Task<HttpResponseMessage> SaveAnswersAsync(HttpClient client, string attemptId, string blockId, string answer, long revision = 1) =>
        client.PutAsJsonAsync($"/api/attempts/{attemptId}/answers", new
        {
            revision,
            answers = new[]
            {
                new { blockId, answer }
            }
        });

    private static async Task EnsureInitializedAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void SetResumeCredential(HttpClient client, StartAttemptResponse attempt)
    {
        client.DefaultRequestHeaders.Remove("Authorization");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {attempt.ResumeCredential}");
    }

    private sealed record StartAttemptResponse(StudentAttempt Attempt, IReadOnlyList<LocalExamBlock> Blocks, string ResumeCredential, string CredentialExpiresAt);

    private sealed class LocalApiFactory : WebApplicationFactory<Program>
    {
        public const string ExamVersionId = "test-matematica-6-v1";
        public string QuestionBlockId { get; } = "test-question-1";
        private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"plancope-local-{Guid.NewGuid():N}.db");
        private readonly string? previousConnectionString;
        private readonly string? previousSeedDemoExam;
        private string ConnectionString => $"Data Source={databasePath};Pooling=False";

        public LocalApiFactory()
        {
            previousConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__LocalDatabase");
            previousSeedDemoExam = Environment.GetEnvironmentVariable("Local__SeedDemoExam");
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", ConnectionString);
            Environment.SetEnvironmentVariable("Local__SeedDemoExam", "false");
        }

        public SqliteConnection CreateConnection()
        {
            var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            return connection;
        }

        public void SeedRevokedNode()
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO node_identity (id, node_id, cue, fingerprint_hash, fingerprint_components_json, enrolled_at, last_sync_at, credential_state, revocation_detected_at, revocation_stage) VALUES ('revoked-test', 'node-test', '180055400', 'hash', '{}', $now, $now, 'revoked', $now, NULL);";
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        public void SeedExam()
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();

            Execute(connection, transaction, """
                INSERT INTO local_exam_versions (id, remote_exam_version_id, exam_code, version_number, checksum, metadata_json, schema_version, synced_at)
                VALUES ($id, $remoteId, $code, 1, 'test-checksum', '{"title":"Matematica 6","grade":"6","division":"A"}', 1, $now);
                """,
                ("$id", ExamVersionId),
                ("$remoteId", "remote-test-matematica-6-v1"),
                ("$code", "MAT-6-TEST"),
                ("$now", DateTimeOffset.UtcNow.ToString("O")));

            Execute(connection, transaction, """
                INSERT INTO local_exam_blocks (id, local_exam_version_id, remote_block_id, order_index, block_type, config_json, validation_json)
                VALUES ($id, $examId, $remoteBlockId, 0, 'multiple_choice', '{"question":"Cuanto es 18 + 24?","options":[{"value":"42","label":"42"},{"value":"44","label":"44"}]}', NULL);
                """,
                ("$id", QuestionBlockId),
                ("$examId", ExamVersionId),
                ("$remoteBlockId", "remote-test-question-1"));

            transaction.Commit();
        }

        public void SeedRoster(string cue, string schoolYear, string snapshotId, string sectionId, string status)
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO schools (cue, created_at) VALUES ($cue, $fetched);
                """,
                ("$cue", cue), ("$fetched", DateTimeOffset.UtcNow.ToString("O")));
            Execute(connection, transaction, """
                INSERT INTO local_roster_snapshots (id, cue, school_year, fetched_at, checksum, section_count, student_count, status)
                VALUES ($id, $cue, $year, $fetched, $checksum, 1, 2, $status);
                """,
                ("$id", snapshotId), ("$cue", cue), ("$year", schoolYear),
                ("$fetched", DateTimeOffset.UtcNow.ToString("O")), ("$checksum", $"checksum-{snapshotId}"), ("$status", status));
            Execute(connection, transaction, """
                INSERT INTO local_roster_sections (id, snapshot_id, ge_section_id, course, division, level, shift)
                VALUES ($id, $snapshot, 100, '6º', 'A', 'Primario', 'Mañana');
                """,
                ("$id", sectionId), ("$snapshot", snapshotId));
            transaction.Commit();
        }

        public void SetSchoolName(string cue, string name)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE schools SET name = $name WHERE cue = $cue;";
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$cue", cue);
            command.ExecuteNonQuery();
        }

        public void SetExamTitle(string title)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE local_exam_versions SET metadata_json = json_set(metadata_json, '$.title', $title) WHERE id = $id;";
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$id", ExamVersionId);
            command.ExecuteNonQuery();
        }

        public void SetSectionShift(string sectionId, string shift)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE local_roster_sections SET shift = $shift WHERE id = $id;";
            command.Parameters.AddWithValue("$shift", shift);
            command.Parameters.AddWithValue("$id", sectionId);
            command.ExecuteNonQuery();
        }

        public void SetSectionDivision(string sectionId, string division)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE local_roster_sections SET division = $division WHERE id = $id;";
            command.Parameters.AddWithValue("$division", division);
            command.Parameters.AddWithValue("$id", sectionId);
            command.ExecuteNonQuery();
        }

        public void SetSectionCourse(string sectionId, string course)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE local_roster_sections SET course = $course WHERE id = $id;";
            command.Parameters.AddWithValue("$course", course);
            command.Parameters.AddWithValue("$id", sectionId);
            command.ExecuteNonQuery();
        }

        public string ExplainSchoolAndStatusHistoryPlan(string cue, string status)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                EXPLAIN QUERY PLAN
                SELECT s.id FROM delivery_sessions s
                WHERE s.school_code = $school AND s.status = $status
                ORDER BY s.start_at DESC, s.id DESC LIMIT 20;
                """;
            command.Parameters.AddWithValue("$school", cue);
            command.Parameters.AddWithValue("$status", status);
            using var reader = command.ExecuteReader();
            var details = new List<string>();
            while (reader.Read()) details.Add(reader.GetString(3));
            return string.Join(Environment.NewLine, details);
        }

        public void SetAccessCode(string sessionId, string accessCode)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE delivery_sessions SET access_code = $accessCode WHERE id = $id;";
            command.Parameters.AddWithValue("$accessCode", accessCode);
            command.Parameters.AddWithValue("$id", sessionId);
            command.ExecuteNonQuery();
        }

        public string SeedSearchSessions(int count)
        {
            const string expectedId = "search-perf-target";
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();
            using (var schoolCommand = connection.CreateCommand())
            {
                schoolCommand.Transaction = transaction;
                schoolCommand.CommandText = "INSERT INTO schools (cue, name) VALUES ($cue, $name);";
                var cueParameter = schoolCommand.Parameters.Add("$cue", SqliteType.Text);
                var nameParameter = schoolCommand.Parameters.Add("$name", SqliteType.Text);
                for (var school = 0; school < 30; school++)
                {
                    cueParameter.Value = $"1800554{school:00}";
                    nameParameter.Value = school == 0 ? "Escuela N° 123 \"Dr. Juan Pujol\"" : $"Escuela {school}";
                    schoolCommand.ExecuteNonQuery();
                }
            }

            using (var sessionCommand = connection.CreateCommand())
            {
                sessionCommand.Transaction = transaction;
                sessionCommand.CommandText = """
                    INSERT INTO delivery_sessions (id, exam_version_id, school_code, classroom_code, started_by, start_at, status, access_code, expected_student_count)
                    VALUES ($id, $exam, $cue, '6 A', 'Test', $start, 'active', $access, 1);
                    """;
                var idParameter = sessionCommand.Parameters.Add("$id", SqliteType.Text);
                var cueParameter = sessionCommand.Parameters.Add("$cue", SqliteType.Text);
                var startParameter = sessionCommand.Parameters.Add("$start", SqliteType.Text);
                var accessParameter = sessionCommand.Parameters.Add("$access", SqliteType.Text);
                sessionCommand.Parameters.AddWithValue("$exam", ExamVersionId);
                sessionCommand.Prepare();
                for (var index = 0; index < count; index++)
                {
                    idParameter.Value = index == 0 ? expectedId : $"search-perf-{index:D5}";
                    cueParameter.Value = index == 0 ? "180055400" : $"1800554{1 + ((index - 1) % 29):00}";
                    startParameter.Value = DateTimeOffset.UtcNow.AddSeconds(-index).ToString("O");
                    accessParameter.Value = $"P{index:D5}Q";
                    sessionCommand.ExecuteNonQuery();
                }
            }
            transaction.Commit();
            return expectedId;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:LocalDatabase"] = ConnectionString,
                    ["Local:SeedDemoExam"] = "false",
                    ["Nominalization:DocumentHmacKey"] = LocalApiFactory.DocumentHmacKey
                });
            });
        }

        public const string DocumentHmacKey = "release-test-key-with-at-least-32-bytes";

        public void SeedRosterStudent(string snapshotId, string sectionId, string id, int gePersonId, string document, string firstName, string lastName)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO local_roster_students (id, snapshot_id, section_id, ge_person_id, document_hash, document_last4, first_name, last_name)
                VALUES ($id, $snapshot, $section, $person, $hash, $last4, $first, $last);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$snapshot", snapshotId);
            command.Parameters.AddWithValue("$section", sectionId);
            command.Parameters.AddWithValue("$person", gePersonId);
            command.Parameters.AddWithValue("$hash", ComputeDocumentHash(document));
            command.Parameters.AddWithValue("$last4", "5678");
            command.Parameters.AddWithValue("$first", firstName);
            command.Parameters.AddWithValue("$last", lastName);
            command.ExecuteNonQuery();
        }

        public void SeedAttempt(string sessionId, string attemptId, string? rosterStudentId, int gePersonId, string status, string startedAt, string? submittedAt = null, string? firstName = null, string? lastName = null, string? extraStudentId = null)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO student_attempts (id, delivery_session_id, student_code, status, started_at, submitted_at, local_sequence,
                    roster_student_id, ge_person_id, student_first_name, student_last_name, document_last4, extra_student_id)
                VALUES ($id, $session, $id, $status, $started, $submitted, 1, $roster, $person, $first, $last, '9876', $extra);
                """;
            command.Parameters.AddWithValue("$id", attemptId);
            command.Parameters.AddWithValue("$session", sessionId);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$started", startedAt);
            command.Parameters.AddWithValue("$submitted", (object?)submittedAt ?? DBNull.Value);
            command.Parameters.AddWithValue("$roster", (object?)rosterStudentId ?? DBNull.Value);
            command.Parameters.AddWithValue("$person", gePersonId);
            command.Parameters.AddWithValue("$first", (object?)firstName ?? DBNull.Value);
            command.Parameters.AddWithValue("$last", (object?)lastName ?? DBNull.Value);
            command.Parameters.AddWithValue("$extra", (object?)extraStudentId ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        public void SetRosterSection(string sectionId, string? course, string? division, string? shift, string? level)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE local_roster_sections SET course = $course, division = $division, shift = $shift, level = $level WHERE id = $id;";
            command.Parameters.AddWithValue("$course", (object?)course ?? DBNull.Value);
            command.Parameters.AddWithValue("$division", (object?)division ?? DBNull.Value);
            command.Parameters.AddWithValue("$shift", (object?)shift ?? DBNull.Value);
            command.Parameters.AddWithValue("$level", (object?)level ?? DBNull.Value);
            command.Parameters.AddWithValue("$id", sectionId);
            command.ExecuteNonQuery();
        }

        public void DeleteRosterStudents(string sectionId)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM local_roster_students WHERE section_id = $id;";
            command.Parameters.AddWithValue("$id", sectionId);
            command.ExecuteNonQuery();
        }

        private static string ComputeDocumentHash(string document)
        {
            var normalized = new string(document.Where(char.IsDigit).ToArray());
            return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(DocumentHmacKey), Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        }

        public void ExpireResolution(string token)
        {
            using var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE student_resolutions SET expires_at = $expires WHERE token_hash = $hash;";
            command.Parameters.AddWithValue("$expires", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
            command.Parameters.AddWithValue("$hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant());
            command.ExecuteNonQuery();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Environment.SetEnvironmentVariable("ConnectionStrings__LocalDatabase", previousConnectionString);
            Environment.SetEnvironmentVariable("Local__SeedDemoExam", previousSeedDemoExam);
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }

        private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, string Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (var parameter in parameters)
            {
                command.Parameters.AddWithValue(parameter.Name, parameter.Value);
            }

            command.ExecuteNonQuery();
        }
    }
}
