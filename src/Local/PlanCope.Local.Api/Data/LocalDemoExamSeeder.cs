using System.Text;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Local.Api.Services;
using PlanCope.Shared.Domain;
using PlanCope.Shared.Domain.Local;

namespace PlanCope.Local.Api.Data;

public sealed class LocalDemoExamSeeder(ILocalExamRepository repository, LocalAssetFileService assetFileService)
{
    public void SeedIfEmpty()
    {
        SeedDemoExamIfMissing();
        SeedExtensiveExamIfMissing();
    }

    private void SeedDemoExamIfMissing()
    {
        const string examId = "demo-matematica-6-v1";
        if (repository.GetByIdAsync(examId).GetAwaiter().GetResult() is not null)
        {
            return;
        }

        const string assetId = "demo-mat-6-figura-1";
        var now = DateTimeOffset.UtcNow.ToString("O");
        var asset = SaveSvgAsset(
            examId,
            assetId,
            "figura-demo.svg",
            """
            <svg xmlns="http://www.w3.org/2000/svg" width="520" height="220" viewBox="0 0 520 220">
              <rect width="520" height="220" fill="#f7f8f5"/>
              <rect x="70" y="45" width="150" height="90" fill="#d9f0ee" stroke="#0f766e" stroke-width="4"/>
              <rect x="300" y="45" width="90" height="90" fill="#fff4cc" stroke="#8a6d00" stroke-width="4"/>
              <text x="95" y="165" font-family="Arial" font-size="22" fill="#17202a">Rectangulo</text>
              <text x="315" y="165" font-family="Arial" font-size="22" fill="#17202a">Cuadrado</text>
            </svg>
            """,
            now);

        var blocks = new[]
        {
            TextBlock(examId, "demo-mat-6-bienvenida", 0, "Lee cada consigna con atencion. Puedes revisar tus respuestas antes de enviar."),
            MultipleChoiceBlock(examId, "demo-mat-6-multiple-1", 1, "Cuanto es 18 + 24?", [("38", "38"), ("42", "42"), ("44", "44")], required: true),
            ImageBlock(examId, "demo-mat-6-imagen-1", 2, assetId, "Rectangulo y cuadrado", "Observa las figuras antes de responder."),
            TrueFalseBlock(examId, "demo-mat-6-truefalse-1", 3, "El numero 9 es multiplo de 3.", required: true),
            ShortAnswerBlock(examId, "demo-mat-6-short-1", 4, "Explica brevemente como resolverias 15 x 4.", required: true)
        };

        var exam = new LocalExamVersion(
            examId,
            "demo-remote-matematica-6-v1",
            "MAT-6-DEMO",
            1,
            "demo",
            """{"title":"Matematica 6 - Demo","grade":"6","division":"A","subject":"Matematica","fallback":true}""",
            1,
            now);

        repository.UpsertImportedExamAsync(exam, blocks, [asset], []).GetAwaiter().GetResult();
    }

    private void SeedExtensiveExamIfMissing()
    {
        const string examId = "demo-integrado-6-v1";
        if (repository.GetByIdAsync(examId).GetAwaiter().GetResult() is not null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var assets = new[]
        {
            SaveSvgAsset(examId, "mapa-rio-estaciones", "mapa-rio-estaciones.svg", SvgAssets.MapaRio, now),
            SaveSvgAsset(examId, "grafico-botellas", "grafico-botellas.svg", SvgAssets.GraficoBotellas, now),
            SaveSvgAsset(examId, "figuras-geometricas", "figuras-geometricas.svg", SvgAssets.FigurasGeometricas, now)
        };

        var blocks = new[]
        {
            TextBlock(examId, "intro-general", 0, "Este examen integrado combina lectura, interpretacion de imagenes, matematica y ciencias. Responde con calma. Algunas preguntas son obligatorias y otras sirven para probar guardado parcial."),
            TextBlock(examId, "seccion-lectura", 1, "Lee el siguiente texto: En una escuela cercana al rio, los estudiantes organizaron una jornada de observacion ambiental. Durante cuatro dias midieron residuos recolectados, dibujaron el recorrido del agua y compararon formas geometricas presentes en carteles y recipientes."),
            MultipleChoiceBlock(examId, "q-lectura-proposito", 2, "Cual fue el proposito principal de la jornada de observacion ambiental?", [("competencia", "Organizar una competencia deportiva"), ("ambiente", "Observar y registrar informacion del ambiente"), ("viaje", "Preparar un viaje de egresados"), ("venta", "Vender materiales reciclables")], required: true),
            ShortAnswerBlock(examId, "q-lectura-explica", 3, "Explica en una o dos oraciones por que registrar datos durante varios dias puede mejorar una observacion.", required: true),
            ImageBlock(examId, "mapa-rio", 4, "mapa-rio-estaciones", "Recorrido de un rio con tres estaciones de medicion", "Imagen 1. Recorrido del rio y tres estaciones de medicion."),
            MultipleChoiceBlock(examId, "q-mapa-estaciones", 5, "Segun la imagen 1, cuantas estaciones de medicion se muestran?", [("2", "Dos"), ("3", "Tres"), ("4", "Cuatro"), ("5", "Cinco")], required: true),
            TrueFalseBlock(examId, "q-mapa-vf", 6, "La imagen 1 muestra el recorrido del rio con una linea curva.", required: true),
            ImageBlock(examId, "grafico-botellas", 7, "grafico-botellas", "Grafico de barras de botellas recolectadas por dia", "Imagen 2. Botellas recolectadas entre lunes y jueves."),
            MultipleChoiceBlock(examId, "q-grafico-maximo", 8, "Que dia se recolectaron mas botellas?", [("lunes", "Lunes"), ("martes", "Martes"), ("miercoles", "Miercoles"), ("jueves", "Jueves")], required: true),
            ShortAnswerBlock(examId, "q-grafico-total", 9, "Suma las botellas de lunes, martes, miercoles y jueves. Escribe el total y muestra la cuenta.", required: true),
            TrueFalseBlock(examId, "q-grafico-compara", 10, "El martes se recolectaron 10 botellas mas que el lunes.", required: true),
            ImageBlock(examId, "figuras", 11, "figuras-geometricas", "Rectangulo, triangulo y circulo", "Imagen 3. Tres figuras geometricas identificadas como A, B y C."),
            MultipleChoiceBlock(examId, "q-figuras-a", 12, "Que figura corresponde a la Figura A?", [("triangulo", "Triangulo"), ("rectangulo", "Rectangulo"), ("circulo", "Circulo"), ("rombo", "Rombo")], required: true),
            TrueFalseBlock(examId, "q-figuras-c", 13, "La Figura C es un circulo.", required: true),
            ShortAnswerBlock(examId, "q-opcional-observacion", 14, "Pregunta opcional: escribe una observacion adicional sobre cualquiera de las imagenes.", required: false),
            MultipleChoiceBlock(examId, "q-cierre-autoevaluacion", 15, "Como te resulto este examen de prueba?", [("claro", "Claro y facil de seguir"), ("medio", "Entendible, con algunas dudas"), ("dificil", "Dificil de completar")], required: false)
        };

        var exam = new LocalExamVersion(
            examId,
            "demo-remote-integrado-6-v1",
            "INT-6-DEMO-EXT",
            1,
            "demo-extensive",
            """{"title":"Examen integrado 6 - Demo extenso","grade":"6","division":"Demo","subject":"Matematica, Ciencias y Lengua","fallback":true}""",
            1,
            now);

        repository.UpsertImportedExamAsync(exam, blocks, assets, []).GetAwaiter().GetResult();
    }

    private LocalAsset SaveSvgAsset(string examId, string assetId, string fileName, string svg, string syncedAt)
    {
        var saved = assetFileService
            .SaveAsync(assetId, fileName, "image/svg+xml", Encoding.UTF8.GetBytes(svg), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return new LocalAsset(assetId, $"demo-remote:{assetId}", examId, fileName, saved.MimeType, saved.Checksum, saved.Path, syncedAt);
    }

    private static LocalExamBlock TextBlock(string examId, string id, int order, string content)
    {
        return Block(examId, id, order, BlockType.Text, $$"""{"content":{{JsonString(content)}}}""", null);
    }

    private static LocalExamBlock ImageBlock(string examId, string id, int order, string assetId, string alt, string caption)
    {
        return Block(examId, id, order, BlockType.Image, $$"""{"assetId":{{JsonString(assetId)}},"alt":{{JsonString(alt)}},"caption":{{JsonString(caption)}}}""", null);
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

    private static LocalExamBlock ShortAnswerBlock(string examId, string id, int order, string prompt, bool required)
    {
        return Block(examId, id, order, BlockType.ShortAnswer, $$"""{"prompt":{{JsonString(prompt)}}}""", RequiredJson(required));
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

    private static class SvgAssets
    {
        public const string MapaRio = """
            <svg xmlns="http://www.w3.org/2000/svg" width="720" height="360" viewBox="0 0 720 360">
              <rect width="720" height="360" fill="#f7f8f5"/>
              <path d="M90 240 C150 160, 210 180, 270 110 S420 80, 500 150 S610 180, 650 95" fill="none" stroke="#0f766e" stroke-width="8"/>
              <circle cx="150" cy="175" r="18" fill="#d38b25"/>
              <circle cx="355" cy="88" r="18" fill="#d38b25"/>
              <circle cx="585" cy="167" r="18" fill="#d38b25"/>
              <text x="118" y="288" font-family="Arial" font-size="24" fill="#17202a">Recorrido del rio y tres estaciones de medicion</text>
            </svg>
            """;

        public const string GraficoBotellas = """
            <svg xmlns="http://www.w3.org/2000/svg" width="720" height="380" viewBox="0 0 720 380">
              <rect width="720" height="380" fill="#ffffff"/>
              <line x1="90" y1="310" x2="650" y2="310" stroke="#1a2732" stroke-width="3"/>
              <line x1="90" y1="60" x2="90" y2="310" stroke="#1a2732" stroke-width="3"/>
              <rect x="140" y="210" width="65" height="100" fill="#0f766e"/>
              <rect x="260" y="160" width="65" height="150" fill="#0f766e"/>
              <rect x="380" y="110" width="65" height="200" fill="#0f766e"/>
              <rect x="500" y="185" width="65" height="125" fill="#0f766e"/>
              <text x="135" y="340" font-family="Arial" font-size="20">Lun</text>
              <text x="255" y="340" font-family="Arial" font-size="20">Mar</text>
              <text x="375" y="340" font-family="Arial" font-size="20">Mie</text>
              <text x="495" y="340" font-family="Arial" font-size="20">Jue</text>
              <text x="160" y="198" font-family="Arial" font-size="18">20</text>
              <text x="280" y="148" font-family="Arial" font-size="18">30</text>
              <text x="400" y="98" font-family="Arial" font-size="18">40</text>
              <text x="520" y="173" font-family="Arial" font-size="18">25</text>
              <text x="180" y="42" font-family="Arial" font-size="24" fill="#17202a">Botellas recolectadas por dia</text>
            </svg>
            """;

        public const string FigurasGeometricas = """
            <svg xmlns="http://www.w3.org/2000/svg" width="720" height="320" viewBox="0 0 720 320">
              <rect width="720" height="320" fill="#f8fbfa"/>
              <rect x="80" y="70" width="150" height="90" fill="#d9f0ee" stroke="#0f766e" stroke-width="5"/>
              <polygon points="360,55 450,205 270,205" fill="#fff4cc" stroke="#8a6d00" stroke-width="5"/>
              <circle cx="590" cy="145" r="62" fill="#f7d7ce" stroke="#a13d2d" stroke-width="5"/>
              <text x="93" y="210" font-family="Arial" font-size="22">Figura A</text>
              <text x="322" y="245" font-family="Arial" font-size="22">Figura B</text>
              <text x="548" y="245" font-family="Arial" font-size="22">Figura C</text>
            </svg>
            """;
    }
}
