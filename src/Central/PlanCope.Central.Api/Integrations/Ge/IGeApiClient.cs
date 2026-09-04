namespace PlanCope.Central.Api.Integrations.Ge;

public interface IGeApiClient
{
    Task<GeStudentIdentity?> FindStudentByDocumentAsync(
        string document,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GeRosterStudent>> GetStudentsBySchoolAsync(
        string cue,
        string schoolYear,
        CancellationToken cancellationToken = default);

}
