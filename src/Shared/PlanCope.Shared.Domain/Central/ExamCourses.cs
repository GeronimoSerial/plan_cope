namespace PlanCope.Shared.Domain.Central;

/// <summary>Stable course keys accepted by exam authoring and used as publication grade tags.</summary>
public static class ExamCourses
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["primaria-1"] = "Primaria 1° grado",
        ["primaria-2"] = "Primaria 2° grado",
        ["primaria-3"] = "Primaria 3° grado",
        ["primaria-4"] = "Primaria 4° grado",
        ["primaria-5"] = "Primaria 5° grado",
        ["primaria-6"] = "Primaria 6° grado",
        ["secundaria-1"] = "Secundaria 1° año",
        ["secundaria-2"] = "Secundaria 2° año",
        ["secundaria-3"] = "Secundaria 3° año",
        ["secundaria-4"] = "Secundaria 4° año",
        ["secundaria-5"] = "Secundaria 5° año",
        ["secundaria-6"] = "Secundaria 6° año"
    };

    public static bool IsValid(string? key) => key is not null && Labels.ContainsKey(key);
}
