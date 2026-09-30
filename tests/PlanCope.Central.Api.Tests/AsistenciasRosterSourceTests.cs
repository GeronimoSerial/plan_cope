using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PlanCope.Central.Api.Integrations.Ge;
using Testcontainers.PostgreSql;
using Xunit;

namespace PlanCope.Central.Api.Tests;

public sealed class AsistenciasRosterSourceTests
{
    [DockerFact]
    public async Task Reads_current_active_school_students_and_skips_invalid_nominal_rows()
    {
        await using var postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await postgres.StartAsync();
        await using var dataSource = NpgsqlDataSource.Create(postgres.GetConnectionString());
        const string year = "2026";
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("""
                CREATE TABLE secciones (
                    id integer PRIMARY KEY, cue_anexo text, ciclo_lectivo text, curso text, division text,
                    turno text, nivel_ensenanza text, es_valido boolean, status text);
                CREATE TABLE alumnos (
                    id integer PRIMARY KEY, persona_id integer, seccion_id integer, status text, ciclo_lectivo text);
                CREATE TABLE personas (
                    persona_id integer PRIMARY KEY, apellido text, nombre text, tipo_documento text,
                    nro_documento text, sexo text, fecha_nacimiento date);
                INSERT INTO secciones VALUES
                    (1, '1800000-01', @Year, repeat('C', 80), 'A', 'Mañana', repeat('N', 140), true, 'activo'),
                    (2, '1800000-01', @Year, '6', 'B', 'Tarde', 'Primario', false, 'activo'),
                    (3, '1800000-01', @Year, '6', 'C', 'Tarde', 'Primario', true, 'inactivo'),
                    (4, '1800001-00', @Year, '6', 'D', 'Tarde', 'Primario', true, 'activo'),
                    (5, '1800000-01', @Year, '6', 'E', 'Tarde', 'Primario', true, 'activo');
                INSERT INTO personas VALUES
                    (1001, '  Pérez  ', repeat('A', 260), 'DNI', '12.345.678.901.234.567.890.123.456.789.012.34', 'X', NULL),
                    (1002, 'Sin Documento', 'Ana', 'DNI', 'sin numero', 'X', NULL),
                    (1003, '', 'Sin Apellido', 'DNI', '22333444', 'X', NULL),
                    (1004, 'Inactiva', 'Eva', 'DNI', '33444555', 'X', NULL),
                    (1005, 'Año anterior', 'Leo', 'DNI', '44555666', 'X', NULL);
                INSERT INTO alumnos VALUES
                    (1, 1001, 1, 'activo', @Year),
                    (2, 1002, 1, 'activo', @Year),
                    (3, 1003, 1, 'activo', @Year),
                    (4, 1004, 1, 'inactivo', @Year),
                    (5, 1005, 5, 'activo', '2025');
                """, new { Year = year });
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rosters:SchoolYear"] = year
        }).Build();
        var source = new AsistenciasRosterSource(dataSource, configuration, NullLogger<AsistenciasRosterSource>.Instance);

        var students = await source.GetStudentsBySchoolAsync("180000001", year);
        var student = Assert.Single(students);

        Assert.Equal(1001, student.PersonaId);
        Assert.Equal(1, student.EstablecimientoCursoDivisionId);
        Assert.Equal("1800000-01", student.CueAnexo);
        Assert.Equal(new string('C', 64), student.Curso);
        Assert.Equal(new string('N', 128), student.NivelEnsenanza);
        Assert.Equal("Pérez", student.Apellido);
        Assert.Equal(256, student.Nombre!.Length);
        Assert.Equal(32, student.NroDocumento!.Length);
        var identity = await source.FindStudentByDocumentAsync("12.345.678.901.234.567.890.123.456.789.012.34");
        Assert.Equal(1001, identity?.PersonaId);
    }
}

public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")) && !File.Exists("/var/run/docker.sock"))
            Skip = "Docker is not available; the Testcontainers PostgreSQL fixture requires a Docker daemon.";
    }
}
