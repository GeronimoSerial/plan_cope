using System.Text.Json.Serialization;

namespace PlanCope.Central.Api.Integrations.Ge;

internal sealed class GeTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }
}

internal sealed class GePersonResponse
{
    [JsonPropertyName("personaId")]
    public int PersonaId { get; init; }

    [JsonPropertyName("apellido")]
    public string? Apellido { get; init; }

    [JsonPropertyName("nombre")]
    public string? Nombre { get; init; }

    [JsonPropertyName("nroDocumento")]
    public string? NroDocumento { get; init; }
}

internal sealed class GeRosterStudentResponse
{
    [JsonPropertyName("personaId")]
    public int PersonaId { get; init; }

    [JsonPropertyName("establecimientoCursoDivisionId")]
    public int? EstablecimientoCursoDivisionId { get; init; }

    [JsonPropertyName("cueAnexo")]
    public string? CueAnexo { get; init; }

    [JsonPropertyName("curso")]
    public string? Curso { get; init; }

    [JsonPropertyName("division")]
    public string? Division { get; init; }

    [JsonPropertyName("nivelEnsenanza")]
    public string? NivelEnsenanza { get; init; }

    [JsonPropertyName("turno")]
    public string? Turno { get; init; }

    [JsonPropertyName("persona")]
    public GePersonResponse? Persona { get; init; }

    [JsonPropertyName("apellido")]
    public string? Apellido { get; init; }

    [JsonPropertyName("nombre")]
    public string? Nombre { get; init; }

    [JsonPropertyName("nroDocumento")]
    public string? NroDocumento { get; init; }
}

internal sealed class GeSectionResponse
{
    [JsonPropertyName("establecimientoCursoDivisionId")]
    public int EstablecimientoCursoDivisionId { get; init; }

    [JsonPropertyName("cueAnexo")]
    public string? CueAnexo { get; init; }

    [JsonPropertyName("curso")]
    public string? Curso { get; init; }

    [JsonPropertyName("division")]
    public string? Division { get; init; }

    [JsonPropertyName("nivelEnsenanza")]
    public string? NivelEnsenanza { get; init; }

    [JsonPropertyName("turno")]
    public string? Turno { get; init; }
}
