using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data;

public sealed class LocalDemoExamSeeder(ILocalExamRepository repository)
{
    public void SeedIfEmpty()
    {
        SeedDemoExamIfMissing();
        SeedExtensiveExamIfMissing();
    }

    private void SeedDemoExamIfMissing()
    {
        const string examId = "demo-matematica-6-v1";
        if (repository.GetByIdAsync(examId).GetAwaiter().GetResult() is not null &&
            repository.GetBlocksAsync(examId).GetAwaiter().GetResult().Count > 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToString("O");

        var blocks = new[]
        {
            MultipleChoiceBlock(examId, "demo-mat-6-multiple-1", 0, "Cuanto es 18 + 24?", [("38", "38"), ("42", "42"), ("44", "44")], required: true),
            TrueFalseBlock(examId, "demo-mat-6-truefalse-1", 1, "El numero 9 es multiplo de 3.", required: true),
            TrueFalseBlock(examId, "demo-mat-6-truefalse-2", 2, "15 x 4 es igual a 60.", required: true)
        };

        var exam = new LocalExamVersion(
            examId,
            "demo-remote-matematica-6-v1",
            "MAT-6-DEMO",
            1,
            "demo",
            """{"title":"Matematica 6 - Demo","grade":"6","division":"A","subject":"Matematica","fallback":true}""",
            1,
            now,
            null);

        repository.UpsertImportedExamAsync(exam, blocks, [], []).GetAwaiter().GetResult();
    }

    private void SeedExtensiveExamIfMissing()
    {
        const string examId = "demo-integrado-6-v1";
        if (repository.GetByIdAsync(examId).GetAwaiter().GetResult() is not null &&
            repository.GetBlocksAsync(examId).GetAwaiter().GetResult().Count > 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var blocks = new[]
        {
            MultipleChoiceBlock(examId, "q-lectura-proposito", 0, "En una jornada escolar se registraron residuos durante cuatro días y se observó el recorrido del agua. ¿Cuál fue el propósito principal?", [("competencia", "Organizar una competencia deportiva"), ("ambiente", "Observar y registrar información del ambiente"), ("viaje", "Preparar un viaje"), ("venta", "Vender materiales reciclables")], required: true),
            MultipleChoiceBlock(examId, "q-mapa-estaciones", 1, "Un recorrido de río muestra tres estaciones de medición. ¿Cuántas estaciones hay?", [("2", "Dos"), ("3", "Tres"), ("4", "Cuatro"), ("5", "Cinco")], required: true),
            TrueFalseBlock(examId, "q-mapa-vf", 2, "El recorrido del río está representado por una línea curva.", required: true),
            MultipleChoiceBlock(examId, "q-grafico-maximo", 3, "Según el gráfico de botellas recolectadas durante cuatro días, ¿qué día tuvo el valor más alto?", [("lunes", "Lunes"), ("martes", "Martes"), ("miercoles", "Miércoles"), ("jueves", "Jueves")], required: true),
            TrueFalseBlock(examId, "q-grafico-compara", 4, "El martes se recolectaron 10 botellas más que el lunes.", required: true),
            MultipleChoiceBlock(examId, "q-figuras-a", 5, "¿Qué figura geométrica tiene cuatro lados iguales y cuatro ángulos rectos?", [("triangulo", "Triángulo"), ("rectangulo", "Rectángulo"), ("cuadrado", "Cuadrado"), ("circulo", "Círculo")], required: true),
            TrueFalseBlock(examId, "q-figuras-c", 6, "Un círculo no tiene lados rectos.", required: true),
            MultipleChoiceBlock(examId, "q-cierre-autoevaluacion", 7, "¿Cómo te resultó este examen de prueba?", [("claro", "Claro y fácil de seguir"), ("medio", "Entendible, con algunas dudas"), ("dificil", "Difícil de completar")], required: false)
        };

        var exam = new LocalExamVersion(
            examId,
            "demo-remote-integrado-6-v1",
            "INT-6-DEMO-EXT",
            1,
            "demo-extensive",
            """{"title":"Examen integrado 6 - Demo extenso","grade":"6","division":"Demo","subject":"Matematica, Ciencias y Lengua","fallback":true}""",
            1,
            now,
            null);

        repository.UpsertImportedExamAsync(exam, blocks, [], []).GetAwaiter().GetResult();
    }

    private static LocalExamBlock MultipleChoiceBlock(string examId, string id, int order, string question, IReadOnlyList<(string Value, string Label)> options, bool required)
    {
        var optionJson = string.Join(",", options.Select(static option => $$"""{"value":{{JsonString(option.Value)}},"label":{{JsonString(option.Label)}}}"""));
        return Block(examId, id, order, BlockType.MultipleChoice, $$"""{"question":{{JsonString(question)}},"options":[{{optionJson}}]}""", RequiredJson(required));
    }

    private static LocalExamBlock TrueFalseBlock(string examId, string id, int order, string question, bool required)
    {
        return Block(examId, id, order, BlockType.TrueFalse, $$"""{"question":{{JsonString(question)}}}""", RequiredJson(required));
    }

    private static LocalExamBlock Block(string examId, string id, int order, BlockType type, string configJson, string? validationJson)
    {
        return new LocalExamBlock(id, examId, $"demo-remote:{id}", order, type, configJson, validationJson);
    }

    private static string RequiredJson(bool required)
    {
        return required ? """{"required":true}""" : """{"required":false}""";
    }

    private static string JsonString(string value)
    {
        return System.Text.Json.JsonSerializer.Serialize(value);
    }

}
