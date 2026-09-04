using System.Text.Json;
using Microsoft.Extensions.Options;
using PlanCope.Central.Api.Integrations.Ge;
using PlanCope.Shared.Contracts.Sync;
using PlanCope.Shared.Domain.ValueObjects;

if (args.Length is < 3 or > 4)
{
    Console.Error.WriteLine("Uso: PlanCope.RosterReleaseTool <cue> <ciclo> <archivo-salida> [--inspect-schema]");
    return 2;
}

var schoolYear = args[1].Trim();
var sourceArgument = Path.GetFullPath(args[0]);
var isBatch = File.Exists(sourceArgument);
var cue = isBatch ? string.Empty : CueCode.Normalize(args[0]);
var outputPath = Path.GetFullPath(args[2]);
var username = Environment.GetEnvironmentVariable("GE_API_USERNAME");
var password = Environment.GetEnvironmentVariable("GE_API_PASSWORD");

if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
{
    Console.WriteLine("Ingrese usuario GE y contraseña por entrada estándar:");
    username = Console.ReadLine();
    password = Console.ReadLine();
}

if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
{
    Console.Error.WriteLine("No se recibieron credenciales GE.");
    return 3;
}

var options = Options.Create(new GeApiOptions
{
    BaseUrl = "http://geapi.mec.gob.ar",
    Username = username,
    Password = password,
    PageSize = 100,
    TimeoutSeconds = 60,
    MaxPages = 1_000,
    MaxStudents = 100_000,
    TokenSafetyMarginSeconds = 300,
    PersonBatchSize = ReadIntEnvironment("GE_PERSON_BATCH_SIZE", 5),
    PersonBatchDelaySeconds = ReadIntEnvironment("GE_PERSON_BATCH_DELAY_SECONDS", 2)
});

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(options.Value.BaseUrl.TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds)
};
var tokenProvider = new GeTokenProvider(httpClient, options, new GeTokenCache());
var client = new GeApiClient(httpClient, tokenProvider, options);

if (isBatch)
{
    return await RunBatchAsync(client, sourceArgument, schoolYear, outputPath, args.Length == 4 && args[3] == "--retry-failed");
}

if (args.Length == 4 && args[3] == "--inspect-schema")
{
    var token = await tokenProvider.GetAccessTokenAsync();
    using var request = new HttpRequestMessage(
        HttpMethod.Get,
        $"api/externo/asistencias/GetAlumnosPorSeccionV2?cicloLectivo={Uri.EscapeDataString(schoolYear)}&cue={Uri.EscapeDataString(CueCode.ToGeApiFormat(cue))}&pageIndex=1&pageSize=1");
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    using var response = await httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();
    await using var stream = await response.Content.ReadAsStreamAsync();
    using var schemaDocument = await JsonDocument.ParseAsync(stream);
    PrintSchema(schemaDocument.RootElement, "$", 0);
    return 0;
}

Console.WriteLine($"Consultando una vez el padrón GE para {cue}/{schoolYear}...");
var source = await client.GetStudentsBySchoolAsync(cue, schoolYear);
var build = GeRosterSnapshotBuilder.Build(cue, schoolYear, source);
if (build.Sections.Count == 0 || build.StudentCount == 0)
{
    Console.Error.WriteLine("GE devolvió un padrón vacío; no se generó ningún paquete.");
    return 4;
}

var snapshotId = Guid.NewGuid().ToString("N");
var sections = build.Sections.Select(section =>
{
    var sectionId = Guid.NewGuid().ToString("N");
    var students = section.Students
        .Select(student => new GeRosterStudentPackageDto(
            Guid.NewGuid().ToString("N"),
            sectionId,
            student.GePersonId,
            student.Document,
            student.FirstName,
            student.LastName))
        .ToList();
    return new GeRosterSectionPackageDto(
        sectionId,
        section.GeSectionId,
        section.Course,
        section.Division,
        section.Level,
        section.Shift,
        students);
}).ToList();

var package = new GeRosterPackageDto(
    snapshotId,
    build.Cue,
    build.SchoolYear,
    DateTimeOffset.UtcNow,
    build.Checksum,
    sections.Count,
    build.StudentCount,
    "Ready",
    sections);

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
await File.WriteAllTextAsync(
    outputPath,
    JsonSerializer.Serialize(package, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));

Console.WriteLine($"Paquete generado: {sections.Count} cursos/secciones, {build.StudentCount} alumnos.");
return 0;

static void PrintSchema(JsonElement element, string path, int depth)
{
    if (depth > 8)
    {
        return;
    }

    if (element.ValueKind == JsonValueKind.Object)
    {
        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            Console.WriteLine($"{propertyPath}: {property.Value.ValueKind}");
            PrintSchema(property.Value, propertyPath, depth + 1);
        }
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
        var first = element.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Undefined)
        {
            PrintSchema(first, $"{path}[]", depth + 1);
        }
    }
}

static async Task<int> RunBatchAsync(
    GeApiClient client,
    string cuesPath,
    string schoolYear,
    string outputDirectory,
    bool retryFailed)
{
    var cues = (await File.ReadAllLinesAsync(cuesPath))
        .Where(static line => !string.IsNullOrWhiteSpace(line))
        .Select(CueCode.Normalize)
        .Distinct(StringComparer.Ordinal)
        .ToList();
    Directory.CreateDirectory(outputDirectory);

    var summaryPath = Path.Combine(outputDirectory, $"sync-summary-{schoolYear}.json");
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    var previousSummary = retryFailed && File.Exists(summaryPath)
        ? JsonSerializer.Deserialize<SyncSummary>(await File.ReadAllTextAsync(summaryPath), jsonOptions)
        : null;
    if (retryFailed && previousSummary is not null)
    {
        cues = previousSummary.Failures.Select(static failure => CueCode.Normalize(failure.Cue)).Distinct(StringComparer.Ordinal).ToList();
    }

    Console.WriteLine($"Sincronización global: {cues.Count} CUE, ciclo {schoolYear}.");
    Console.WriteLine("Descargando una sola vez el catálogo global de secciones...");
    var requestedCues = cues.ToHashSet(StringComparer.Ordinal);
    var sections = (await client.GetSectionsAsync())
        .Where(section => CueCode.TryNormalize(section.CueAnexo, out var sectionCue) && requestedCues.Contains(sectionCue))
        .ToDictionary(static section => section.SectionId);
    Console.WriteLine($"Catálogo filtrado: {sections.Count} cursos/secciones.");

    var previousPackages = new Dictionary<string, GeRosterPackageDto>(StringComparer.Ordinal);
    foreach (var path in Directory.EnumerateFiles(outputDirectory, "*.roster.json"))
    {
        try
        {
            var package = JsonSerializer.Deserialize<GeRosterPackageDto>(await File.ReadAllTextAsync(path), jsonOptions);
            if (package is not null && package.Status == "Ready")
            {
                previousPackages[package.Cue] = package;
            }
        }
        catch (JsonException)
        {
            // Un archivo incompleto de una corrida interrumpida no participa de la caché.
        }
    }

    var peopleCache = new Dictionary<int, GeStudentIdentity>();
    var completed = 0;
    var empty = 0;
    var failed = 0;
    var totalStudents = 0;
    var failures = new List<object>();
    foreach (var currentCue in cues)
    {
        try
        {
            var enrollments = await client.GetEnrollmentsBySchoolAsync(currentCue, schoolYear);
            if (enrollments.Count == 0)
            {
                empty++;
                Console.WriteLine($"[{completed + empty + failed}/{cues.Count}] {currentCue}: sin alumnos; se conserva el corte anterior.");
                continue;
            }

            previousPackages.TryGetValue(currentCue, out var previous);
            var previousEnrollments = previous?.Sections
                .SelectMany(section => section.Students.Select(student => (section.GeSectionId, Student: student)))
                .Where(static row => row.GeSectionId.HasValue)
                .ToDictionary(
                    static row => $"{row.Student.GePersonId}:{row.GeSectionId!.Value}",
                    static row => new GeStudentIdentity(row.Student.GePersonId, row.Student.LastName, row.Student.FirstName, row.Student.Document),
                    StringComparer.Ordinal) ?? new Dictionary<string, GeStudentIdentity>(StringComparer.Ordinal);

            var changedIds = enrollments
                .Where(enrollment => !previousEnrollments.ContainsKey($"{enrollment.PersonaId}:{enrollment.SectionId}"))
                .Select(static enrollment => enrollment.PersonaId)
                .Distinct()
                .ToList();
            var uncachedIds = changedIds.Where(id => !peopleCache.ContainsKey(id)).ToList();
            var freshPeople = await client.GetPeopleByIdsAsync(uncachedIds);
            foreach (var person in freshPeople)
            {
                peopleCache[person.Key] = person.Value;
            }
            var missingSections = enrollments.Select(static enrollment => enrollment.SectionId).Distinct().Where(id => !sections.ContainsKey(id)).ToList();
            if (missingSections.Count > 0)
            {
                throw new InvalidOperationException($"Faltan {missingSections.Count} secciones en el catálogo global.");
            }

            var rows = new List<GeRosterStudent>(enrollments.Count);
            foreach (var enrollment in enrollments)
            {
                var enrollmentKey = $"{enrollment.PersonaId}:{enrollment.SectionId}";
                if (!peopleCache.TryGetValue(enrollment.PersonaId, out var person) &&
                    !previousEnrollments.TryGetValue(enrollmentKey, out person))
                {
                    throw new InvalidOperationException($"GE no devolvió datos nominales para la persona {enrollment.PersonaId}.");
                }

                var section = sections[enrollment.SectionId];
                rows.Add(new GeRosterStudent(
                    enrollment.PersonaId,
                    enrollment.SectionId,
                    section.CueAnexo,
                    section.Curso,
                    section.Division,
                    section.NivelEnsenanza,
                    section.Turno,
                    person.Apellido,
                    person.Nombre,
                    person.NroDocumento));
            }

            var build = GeRosterSnapshotBuilder.Build(currentCue, schoolYear, rows);
            var package = CreatePackage(build);
            var finalPath = Path.Combine(outputDirectory, $"{currentCue}-{schoolYear}.roster.json");
            var temporaryPath = finalPath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(package, jsonOptions));
            File.Move(temporaryPath, finalPath, true);
            completed++;
            totalStudents += package.StudentCount;
            Console.WriteLine($"[{completed + empty + failed}/{cues.Count}] {currentCue}: {package.SectionCount} cursos, {package.StudentCount} alumnos ({changedIds.Count} detalles consultados). ");
        }
        catch (Exception exception)
        {
            failed++;
            var geError = exception as GeApiException;
            var error = geError?.StatusCode is { } statusCode
                ? $"GE API request failed ({(int)statusCode} {statusCode})."
                : exception.Message;
            failures.Add(new { cue = currentCue, error });
            Console.Error.WriteLine($"[{completed + empty + failed}/{cues.Count}] {currentCue}: ERROR {error}");
        }
    }

    await File.WriteAllTextAsync(summaryPath, JsonSerializer.Serialize(new
    {
        schoolYear,
        finishedAt = DateTimeOffset.UtcNow,
        requested = previousSummary?.Requested ?? cues.Count,
        completed = (previousSummary?.Completed ?? 0) + completed,
        empty = (previousSummary?.Empty ?? 0) + empty,
        failed,
        totalStudents = (previousSummary?.TotalStudents ?? 0) + totalStudents,
        failures
    }, jsonOptions));
    Console.WriteLine($"Sincronización finalizada: listas={completed}, vacías={empty}, fallidas={failed}, alumnos={totalStudents}.");
    return failed == 0 ? 0 : 5;
}

static int ReadIntEnvironment(string name, int fallback)
{
    return int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
}

static GeRosterPackageDto CreatePackage(GeRosterBuildResult build)
{
    var snapshotId = Guid.NewGuid().ToString("N");
    var sections = build.Sections.Select(section =>
    {
        var sectionId = Guid.NewGuid().ToString("N");
        return new GeRosterSectionPackageDto(
            sectionId,
            section.GeSectionId,
            section.Course,
            section.Division,
            section.Level,
            section.Shift,
            section.Students.Select(student => new GeRosterStudentPackageDto(
                Guid.NewGuid().ToString("N"),
                sectionId,
                student.GePersonId,
                student.Document,
                student.FirstName,
                student.LastName)).ToList());
    }).ToList();
    return new GeRosterPackageDto(
        snapshotId,
        build.Cue,
        build.SchoolYear,
        DateTimeOffset.UtcNow,
        build.Checksum,
        sections.Count,
        build.StudentCount,
        "Ready",
        sections);
}

sealed record SyncSummary(int Requested, int Completed, int Empty, int Failed, int TotalStudents, List<SyncFailure> Failures);

sealed record SyncFailure(string Cue, string Error);
