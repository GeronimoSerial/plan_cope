using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

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
        var seenPeople = new HashSet<string>(StringComparer.Ordinal);
        while (page <= MaxPages)
        {
            var uri = $"api/externo/asistencias/GetPersonasAlumnosPorNroDocumento?nroDocumento={Uri.EscapeDataString(normalizedDocument)}&pageSize={PageSize}&pageIndex={page}";
            using var response = await GetAuthenticatedAsync(uri, cancellationToken);
            var result = await ReadPageAsync<GePersonResponse>(response, cancellationToken);

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

            if (ShouldStop(page, result.Items.Count, result.TotalCount))
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
        if (string.IsNullOrWhiteSpace(cue))
        {
            throw new ArgumentException("CUE is required.", nameof(cue));
        }

        if (string.IsNullOrWhiteSpace(schoolYear))
        {
            throw new ArgumentException("School year is required.", nameof(schoolYear));
        }

        var students = new List<GeRosterStudent>();
        var seenStudents = new HashSet<string>(StringComparer.Ordinal);
        var page = 1;
        while (page <= MaxPages && students.Count < MaxStudents)
        {
            var uri = $"api/externo/asistencias/GetAlumnosPorSeccionV2?cicloLectivo={Uri.EscapeDataString(schoolYear)}&cue={Uri.EscapeDataString(cue)}&pageIndex={page}&pageSize={PageSize}";
            using var response = await GetAuthenticatedAsync(uri, cancellationToken);
            var result = await ReadPageAsync<GeRosterStudentResponse>(response, cancellationToken);

            foreach (var item in result.Items)
            {
                var student = ToRosterStudent(item);
                var document = NormalizeDocument(student.NroDocumento);
                if (seenStudents.Add(CreateRosterIdentityKey(student, document)))
                {
                    students.Add(student);
                    if (students.Count >= MaxStudents)
                    {
                        break;
                    }
                }
            }

            if (ShouldStop(page, result.Items.Count, result.TotalCount))
            {
                break;
            }

            page++;
        }

        return students;
    }

    private async Task<HttpResponseMessage> GetAuthenticatedAsync(string uri, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
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

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = response.StatusCode;
                response.Dispose();
                throw new GeApiException("GE API request failed.", statusCode);
            }

            return response;
        }

        throw new GeApiException("GE API request was unauthorized after token renewal.", HttpStatusCode.Unauthorized);
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

        int? totalCount = null;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("totalRegistros", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("total", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("totalCount", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var parsedTotal))
                {
                    totalCount = parsedTotal;
                }
            }
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array &&
                (IsItemsProperty(property.Name) || LooksLikeStudentArray(property.Value)))
            {
                return new PagedResult<T>(DeserializeArray<T>(property.Value), totalCount);
            }
        }

        // Algunas respuestas de GE devuelven una persona única sin envolverla en una lista.
        if (root.TryGetProperty("personaId", out _))
        {
            var item = root.Deserialize<T>(JsonOptions);
            return item is null ? new PagedResult<T>([], totalCount) : new PagedResult<T>([item], totalCount ?? 1);
        }

        return new PagedResult<T>([], totalCount);
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

    private bool ShouldStop(int page, int itemCount, int? totalCount)
    {
        if (itemCount == 0 || page >= MaxPages)
        {
            return true;
        }

        if (totalCount.HasValue && page * PageSize >= totalCount.Value)
        {
            return true;
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
