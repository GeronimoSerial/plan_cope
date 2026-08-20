namespace PlanCope.Central.Api.Integrations.Ge;

public sealed record GeStudentIdentity(
    int PersonaId,
    string? Apellido,
    string? Nombre,
    string? NroDocumento);

public sealed record GeRosterStudent(
    int PersonaId,
    int? EstablecimientoCursoDivisionId,
    string? CueAnexo,
    string? Curso,
    string? Division,
    string? NivelEnsenanza,
    string? Turno,
    string? Apellido,
    string? Nombre,
    string? NroDocumento);
