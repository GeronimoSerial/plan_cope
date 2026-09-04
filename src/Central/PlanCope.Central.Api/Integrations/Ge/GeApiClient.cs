using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlanCope.Shared.Domain.ValueObjects;

namespace PlanCope.Central.Api.Integrations.Ge;

public sealed class GeApiClient(
    HttpClient httpClient,
    IGeTokenProvider tokenProvider,
    IOptions<GeApiOptions> options) : IGeApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly GeApiOptions _options = options.Value;

    public async Task<GeStudentIdentity?> FindStudentByDocumentAsync(
        string document,
        CancellationToken cancellationToken = default)
    {
        var normalizedDocument = NormalizeDocument(document);
        if (string.IsNullOrWhiteSpace(normalizedDocument))
        {
            throw new ArgumentException("Document must contain at least one digit.", nameof(document));
        }

        var page = 1;
        var receivedItems = 0;
        var seenPeople = new HashSet<string>(StringComparer.Ordinal);
        while (page <= MaxPages)
        {
            var uri = $"api/externo/asistencias/GetPersonasAlumnosPorNroDocumento?nroDocumento={Uri.EscapeDataString(normalizedDocument)}&pageSize={PageSize}&pageIndex={page}";
            using var response = await GetAuthenticatedAsync(uri, cancellationToken);
            var result = await ReadPageAsync<GePersonResponse>(response, cancellationToken);
            receivedItems += result.Items.Count;

            foreach (var person in result.Items)
            {
                var personDocument = NormalizeDocument(person.NroDocumento);
                var personKey = person.PersonaId > 0
                    ? $"p:{person.PersonaId.ToString(CultureInfo.InvariantCulture)}"
                    : $"d:{personDocument}";
                if (!seenPeople.Add(personKey))
                {
                    continue;
                }

                if (string.Equals(normalizedDocument, personDocument, StringComparison.Ordinal))
                {
                    return new GeStudentIdentity(person.PersonaId, person.Apellido, person.Nombre, person.NroDocumento);
                }
            }

            if (ShouldStop(page, result.Items.Count, result.TotalCount, receivedItems))
            {
                break;
            }

            page++;
        }

        return null;
    }

    public async Task<IReadOnlyList<GeRosterStudent>> GetStudentsBySchoolAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        var normalizedCue = CueCode.Normalize(cue);
        var geCue = CueCode.ToGeApiFormat(cue);

        if (string.IsNullOrWhiteSpace(schoolYear))
        {
            throw new ArgumentException("School year is required.", nameof(schoolYear));
        }

        // GE separa el catálogo de cursos, las matrículas y los datos personales.
        // GetAlumnosPorSeccionV2 sólo contiene personaId y secciónId.
        var sections = (await GetSectionsAsync(cancellationToken))
            .Where(section => CueCode.TryNormalize(section.CueAnexo, out var sectionCue) && sectionCue == normalizedCue)
            .ToDictionary(static section => section.SectionId);
        var enrollments = await GetEnrollmentsBySchoolAsync(normalizedCue, schoolYear, cancellationToken);
        var people = await GetPeopleByIdsAsync(enrollments.Select(static item => item.PersonaId), cancellationToken);
        var missingPeople = enrollments.Select(static item => item.PersonaId).Distinct().Where(id => !people.ContainsKey(id)).ToList();
        if (missingPeople.Count > 0)
        {
            throw new GeApiException($"GE did not return nominal data for {missingPeople.Count} enrolled students.");
        }

        return enrollments.Select(enrollment =>
        {
            sections.TryGetValue(enrollment.SectionId, out var section);
            var person = people[enrollment.PersonaId];
            return new GeRosterStudent(
                enrollment.PersonaId,
                enrollment.SectionId,
                section?.CueAnexo ?? geCue,
                section?.Curso,
                section?.Division,
                section?.NivelEnsenanza,
                section?.Turno,
                person.Apellido,
                person.Nombre,
                person.NroDocumento);
        }).ToList();
    }

    public async Task<IReadOnlyList<GeRosterEnrollment>> GetEnrollmentsBySchoolAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default)
    {
        var geCue = CueCode.ToGeApiFormat(cue);
        var enrollments = new List<GeRosterEnrollment>();
        var seenStudents = new HashSet<string>(StringComparer.Ordinal);
        var page = 1;
        var receivedItems = 0;
        while (page <= MaxPages && enrollments.Count < MaxStudents)
        {
            var uri = $"api/externo/asistencias/GetAlumnosPorSeccionV2?cicloLectivo={Uri.EscapeDataString(schoolYear.Trim())}&cue={Uri.EscapeDataString(geCue)}&pageIndex={page}&pageSize={PageSize}";
            using var response = await GetAuthenticatedAsync(uri, cancellationToken);
            var result = await ReadPageAsync<GeRosterStudentResponse>(response, cancellationToken);
            receivedItems += result.Items.Count;

            foreach (var item in result.Items)
            {
                var identityKey = $"{item.PersonaId.ToString(CultureInfo.InvariantCulture)}:{item.EstablecimientoCursoDivisionId?.ToString(CultureInfo.InvariantCulture)}";
                if (item.PersonaId > 0 && item.EstablecimientoCursoDivisionId.HasValue && seenStudents.Add(identityKey))
                {
                    enrollments.Add(new GeRosterEnrollment(item.PersonaId, item.EstablecimientoCursoDivisionId.Value));
                    if (enrollments.Count >= MaxStudents)
                    {
                        break;
                    }
                }
            }

            if (ShouldStop(page, result.Items.Count, result.TotalCount, receivedItems))
            {
                break;
            }

            page++;
        }

        return enrollments;
    }

    public async Task<IReadOnlyList<GeSectionCatalogEntry>> GetSectionsAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<int, GeSectionCatalogEntry>();
        using var response = await GetAuthenticatedAsync("api/externo/asistencias/GetSecciones", cancellationToken);
        var current = await ReadPageAsync<GeSectionResponse>(response, cancellationToken);
        foreach (var section in current.Items)
        {
            if (section.EstablecimientoCursoDivisionId > 0)
            {
                result[section.EstablecimientoCursoDivisionId] = new GeSectionCatalogEntry(
                    section.EstablecimientoCursoDivisionId,
                    section.CueAnexo,
                    section.Curso,
                    section.Division,
                    section.NivelEnsenanza,
                    section.Turno);
            }
        }

        return result.Values.ToList();
    }

    public async Task<IReadOnlyDictionary<int, GeStudentIdentity>> GetPeopleByIdsAsync(
        IEnumerable<int> personIds,
        CancellationToken cancellationToken = default)
    {
        var ids = personIds.Distinct().Order().ToList();
        var result = new Dictionary<int, GeStudentIdentity>();
        var batchSize = Math.Clamp(_options.PersonBatchSize, 1, 50);
        var delaySeconds = Math.Clamp(_options.PersonBatchDelaySeconds, 0, 30);
        for (var offset = 0; offset < ids.Count; offset += batchSize)
        {
            var batch = ids.Skip(offset).Take(batchSize).ToList();
            var people = await Task.WhenAll(batch.Select(id => GetPersonAsync(id, cancellationToken)));
            foreach (var person in people.Where(static person => person is not null))
            {
                result[person!.PersonaId] = new GeStudentIdentity(person.PersonaId, person.Apellido, person.Nombre, person.NroDocumento);
            }

            if (offset + batchSize < ids.Count && delaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        return result;
    }

    private async Task<GePersonResponse?> GetPersonAsync(int personId, CancellationToken cancellationToken)
    {
        var uri = $"api/externo/asistencias/GetPersonasAlumnos?pageSize=100&pageIndex=1&personaId={personId.ToString(CultureInfo.InvariantCulture)}";
        using var response = await GetAuthenticatedAsync(uri, cancellationToken);
        var page = await ReadPageAsync<GePersonResponse>(response, cancellationToken);
        return page.Items.FirstOrDefault(person => person.PersonaId == personId);
    }

    private async Task<HttpResponseMessage> GetAuthenticatedAsync(string uri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                tokenProvider.Invalidate(token);
                continue;
            }

            if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 2)
            {
                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                response.Dispose();
                throw new GeApiException("GE API request failed.", statusCode);
            }

            return response;
        }

        throw new GeApiException("GE API request failed after retries.");
    }

    private async Task<PagedResult<T>> ReadPageAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        return ParsePage<T>(document.RootElement);
    }

    private static PagedResult<T> ParsePage<T>(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return new PagedResult<T>(DeserializeArray<T>(root), null);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new GeApiException("GE API returned an unsupported page format.");
        }

        var totalCount = FindTotalCount(root);
        if (TryFindItemsArray(root, out var items))
        {
            return new PagedResult<T>(DeserializeArray<T>(items), totalCount);
        }

        // Algunas respuestas de GE devuelven una persona única sin envolverla en una lista.
        if (root.TryGetProperty("personaId", out _))
        {
            var item = root.Deserialize<T>(JsonOptions);
            return item is null ? new PagedResult<T>([], totalCount) : new PagedResult<T>([item], totalCount ?? 1);
        }

        return new PagedResult<T>([], totalCount);
    }

    private static int? FindTotalCount(JsonElement element, int depth = 0)
    {
        if (depth > 4 || element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if ((property.Name.Equals("totalRegistros", StringComparison.OrdinalIgnoreCase) ||
                 property.Name.Equals("total", StringComparison.OrdinalIgnoreCase) ||
                 property.Name.Equals("totalCount", StringComparison.OrdinalIgnoreCase)) &&
                property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt32(out var total))
            {
                return total;
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object && FindTotalCount(property.Value, depth + 1) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static bool TryFindItemsArray(JsonElement element, out JsonElement items, int depth = 0)
    {
        if (depth > 4 || element.ValueKind != JsonValueKind.Object)
        {
            items = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array &&
                (IsItemsProperty(property.Name) || LooksLikeStudentArray(property.Value)))
            {
                items = property.Value;
                return true;
            }
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object &&
                (IsItemsProperty(property.Name) || property.Name.Equals("response", StringComparison.OrdinalIgnoreCase)) &&
                TryFindItemsArray(property.Value, out items, depth + 1))
            {
                return true;
            }
        }

        items = default;
        return false;
    }

    private static List<T> DeserializeArray<T>(JsonElement array)
    {
        return array.Deserialize<List<T>>(JsonOptions) ?? [];
    }

    private static bool IsItemsProperty(string propertyName)
    {
        return propertyName.Equals("items", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("data", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("result", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("results", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("lista", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("personas", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("alumnos", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("listaAlumnos", StringComparison.OrdinalIgnoreCase) ||
               propertyName.Equals("alumnosPorSeccion", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeStudentArray(JsonElement array)
    {
        var firstItem = array.EnumerateArray().FirstOrDefault();
        return firstItem.ValueKind == JsonValueKind.Object &&
               (firstItem.TryGetProperty("personaId", out _) || firstItem.TryGetProperty("persona", out _));
    }

    private bool ShouldStop(int page, int itemCount, int? totalCount, int receivedItems)
    {
        if (itemCount == 0 || page >= MaxPages)
        {
            return true;
        }

        if (totalCount.HasValue)
        {
            return receivedItems >= totalCount.Value;
        }

        return itemCount < PageSize;
    }

    private GeRosterStudent ToRosterStudent(GeRosterStudentResponse item)
    {
        var person = item.Persona;
        return new GeRosterStudent(
            person?.PersonaId > 0 ? person.PersonaId : item.PersonaId,
            item.EstablecimientoCursoDivisionId,
            item.CueAnexo,
            item.Curso,
            item.Division,
            item.NivelEnsenanza,
            item.Turno,
            person?.Apellido ?? item.Apellido,
            person?.Nombre ?? item.Nombre,
            person?.NroDocumento ?? item.NroDocumento);
    }

    private static string CreateRosterIdentityKey(GeRosterStudent student, string normalizedDocument)
    {
        var section = student.EstablecimientoCursoDivisionId?.ToString(CultureInfo.InvariantCulture);
        if (student.PersonaId > 0 && section is not null)
        {
            return $"p:{student.PersonaId.ToString(CultureInfo.InvariantCulture)}:s:{section}";
        }

        // Si GE no entrega el identificador de sección, conservar una matrícula por
        // la combinación estable de ubicación disponible y no colapsar cursos distintos.
        var enrollment = string.Join('|',
            student.CueAnexo ?? string.Empty,
            student.Curso ?? string.Empty,
            student.Division ?? string.Empty,
            student.NivelEnsenanza ?? string.Empty,
            student.Turno ?? string.Empty);
        if (student.PersonaId > 0)
        {
            return $"p:{student.PersonaId.ToString(CultureInfo.InvariantCulture)}:e:{enrollment}";
        }

        return $"d:{normalizedDocument}:e:{enrollment}";
    }

    private int PageSize => Math.Clamp(_options.PageSize, 1, 10_000);

    private int MaxPages => Math.Clamp(_options.MaxPages, 1, 100_000);

    private int MaxStudents => Math.Clamp(_options.MaxStudents, 1, 1_000_000);

    private static string NormalizeDocument(string? value)
    {
        return value is null ? string.Empty : new string(value.Where(char.IsDigit).ToArray());
    }

    private sealed record PagedResult<T>(IReadOnlyList<T> Items, int? TotalCount);
}
