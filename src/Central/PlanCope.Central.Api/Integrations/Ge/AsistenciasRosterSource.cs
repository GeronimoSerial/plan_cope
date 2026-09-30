using System.Globalization;
using Dapper;
using Npgsql;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Integrations.Ge;

/// <summary>Read-only roster and identity source backed by the provincial asistencias database.</summary>
public sealed class AsistenciasRosterSource(NpgsqlDataSource dataSource, IConfiguration configuration,
    ILogger<AsistenciasRosterSource> logger) : IGeApiClient
{
    private const int MaxStudentsPerSchool = 1_000_000;

    public async Task<GeStudentIdentity?> FindStudentByDocumentAsync(string document, CancellationToken cancellationToken = default)
    {
        var normalizedDocument = Digits(document);
        if (normalizedDocument.Length == 0)
            throw new ArgumentException("Document must contain at least one digit.", nameof(document));

        var schoolYear = ResolveSchoolYear(configuration);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<IdentityRow>(new CommandDefinition("""
            SELECT p.persona_id AS "PersonaId", p.apellido::text AS "Apellido", p.nombre::text AS "Nombre",
                   p.nro_documento::text AS "NroDocumento"
            FROM personas p
            JOIN alumnos a ON a.persona_id = p.persona_id
            JOIN secciones s ON s.id = a.seccion_id AND s.ciclo_lectivo = a.ciclo_lectivo
            WHERE s.es_valido = TRUE AND lower(s.status::text) = 'activo'
              AND lower(a.status::text) = 'activo'
              AND s.ciclo_lectivo::text = @SchoolYear AND a.ciclo_lectivo::text = @SchoolYear
              AND regexp_replace(COALESCE(p.nro_documento::text, ''), '[^0-9]', '', 'g') = @Document
            ORDER BY p.persona_id
            LIMIT 1;
            """, new { SchoolYear = schoolYear, Document = normalizedDocument }, cancellationToken: cancellationToken,
            commandTimeout: 60));
        return row is null ? null : new GeStudentIdentity(row.PersonaId, row.Apellido, row.Nombre, row.NroDocumento);
    }

    public async Task<IReadOnlyList<GeRosterStudent>> GetStudentsBySchoolAsync(
        string cue, string schoolYear, CancellationToken cancellationToken = default)
    {
        var normalizedCue = CueCode.Normalize(cue);
        var normalizedYear = schoolYear?.Trim() ?? string.Empty;
        if (normalizedYear.Length == 0)
            throw new ArgumentException("School year is required.", nameof(schoolYear));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<RosterRow>(new CommandDefinition("""
            SELECT s.id AS "SectionId", s.cue_anexo::text AS "CueAnexo", s.curso::text AS "Curso",
                   s.division::text AS "Division", s.turno::text AS "Turno", s.nivel_ensenanza::text AS "NivelEnsenanza",
                   p.persona_id AS "PersonaId", p.apellido::text AS "Apellido", p.nombre::text AS "Nombre",
                   p.nro_documento::text AS "NroDocumento"
            FROM secciones s
            JOIN alumnos a ON a.seccion_id = s.id AND a.ciclo_lectivo = s.ciclo_lectivo
            JOIN personas p ON p.persona_id = a.persona_id
            WHERE s.es_valido = TRUE AND lower(s.status::text) = 'activo'
              AND lower(a.status::text) = 'activo'
              AND s.ciclo_lectivo::text = @SchoolYear AND a.ciclo_lectivo::text = @SchoolYear
              AND regexp_replace(COALESCE(s.cue_anexo::text, ''), '[^0-9]', '', 'g') = @Cue
            ORDER BY s.id, p.persona_id;
            """, new { SchoolYear = normalizedYear, Cue = normalizedCue }, cancellationToken: cancellationToken,
            commandTimeout: 120))).ToList();

        var students = new List<GeRosterStudent>(Math.Min(rows.Count, MaxStudentsPerSchool));
        var skipped = 0;
        foreach (var row in rows)
        {
            var document = Digits(row.NroDocumento);
            var firstName = Clean(row.Nombre, 256);
            var lastName = Clean(row.Apellido, 256);
            if (document.Length == 0 || firstName.Length == 0 || lastName.Length == 0)
            {
                skipped++;
                continue;
            }
            if (students.Count >= MaxStudentsPerSchool) break;

            students.Add(new GeRosterStudent(
                row.PersonaId,
                row.SectionId,
                CueCode.ToGeApiFormat(normalizedCue),
                Clean(row.Curso, 64),
                Clean(row.Division, 64),
                Clean(row.NivelEnsenanza, 128),
                Clean(row.Turno, 64),
                lastName,
                firstName,
                document[..Math.Min(document.Length, 32)]));
        }

        if (skipped > 0)
            logger.LogWarning("Skipped {SkippedCount} asistencias roster rows with missing document digits or names for CUE {Cue} and year {SchoolYear}.",
                skipped, normalizedCue, normalizedYear);
        if (rows.Count > MaxStudentsPerSchool)
            logger.LogWarning("Asistencias returned more than {Limit} students for CUE {Cue}; remaining rows were skipped.", MaxStudentsPerSchool, normalizedCue);
        return students;
    }

    public static string ResolveSchoolYear(IConfiguration configuration) =>
        configuration["Rosters:SchoolYear"]?.Trim() is { Length: > 0 } configured
            ? configured
            : TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ResolveTimeZone(configuration)).Year.ToString(CultureInfo.InvariantCulture);

    public static string ResolveSource(IConfiguration configuration)
    {
        var configuredSource = configuration["Rosters:Source"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredSource)) return configuredSource;
        return string.IsNullOrWhiteSpace(configuration.GetConnectionString("Asistencias")) ? "GeApi" : "Asistencias";
    }

    internal static TimeZoneInfo ResolveTimeZone(IConfiguration configuration)
    {
        var timeZoneId = configuration["Rosters:TimeZoneId"]?.Trim();
        if (string.IsNullOrWhiteSpace(timeZoneId)) timeZoneId = "America/Argentina/Buenos_Aires";
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("Argentina Standard Time", TimeSpan.FromHours(-3), "Argentina Standard Time", "Argentina Standard Time"); }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.CreateCustomTimeZone("Argentina Standard Time", TimeSpan.FromHours(-3), "Argentina Standard Time", "Argentina Standard Time"); }
    }

    private static string Digits(string? value) => new((value ?? string.Empty).Where(static character => character is >= '0' and <= '9').ToArray());
    private static string Clean(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed record IdentityRow(int PersonaId, string? Apellido, string? Nombre, string? NroDocumento);
    private sealed record RosterRow(int SectionId, string? CueAnexo, string? Curso, string? Division, string? Turno,
        string? NivelEnsenanza, int PersonaId, string? Apellido, string? Nombre, string? NroDocumento);
}

public sealed class MissingAsistenciasRosterSource(ILogger<MissingAsistenciasRosterSource> logger) : IGeApiClient
{
    private GeApiException NotConfigured()
    {
        logger.LogWarning("Asistencias roster source was selected but ConnectionStrings:Asistencias is not configured.");
        return new GeApiException("The asistencias roster source is not configured.");
    }

    public Task<GeStudentIdentity?> FindStudentByDocumentAsync(string document, CancellationToken cancellationToken = default) =>
        Task.FromException<GeStudentIdentity?>(NotConfigured());

    public Task<IReadOnlyList<GeRosterStudent>> GetStudentsBySchoolAsync(string cue, string schoolYear, CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<GeRosterStudent>>(NotConfigured());
}
