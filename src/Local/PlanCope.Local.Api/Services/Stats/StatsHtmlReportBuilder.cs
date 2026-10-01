using System.Globalization;
using System.Net;
using System.Text;
using PlanCope.Local.Api.Data.Repositories;
using PlanCope.Shared.Domain;

namespace PlanCope.Local.Api.Services.Stats;

public sealed class StatsHtmlReportBuilder
{
    private static readonly Lazy<ReportAssets> Assets = new(LoadAssets, LazyThreadSafetyMode.ExecutionAndPublication);
    public string Build(
        StatsReportDataDto data,
        IReadOnlyList<ExamStatsDto> exams,
        string? schoolYear,
        string? course,
        string? examVersionId,
        DateTimeOffset generatedAt,
        string rosterScope = "school")
    {
        var html = new StringBuilder();
        var attempts = data.Attempts;
        var scored = attempts.Where(attempt => attempt.ScorePercent.HasValue).ToList();
        var average = scored.Count == 0 ? (double?)null : scored.Average(attempt => attempt.ScorePercent!.Value);
        var completion = data.ExpectedStudentCount == 0 ? (double?)null : Math.Min(100, attempts.Count * 100.0 / data.ExpectedStudentCount);
        var missing = Math.Max(0, data.ExpectedStudentCount - attempts.Count);
        var hideStudentRows = SuppressibleValue<int>.For(rosterScope, attempts.Count, attempts.Count).IsSuppressed;

        html.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Informe estadístico · ")
            .Append(E(data.SchoolName)).Append("</title><style>")
            .Append(BuildStyles()).Append("</style></head><body><div class=\"ribbon\" aria-hidden=\"true\"><i></i><i></i><i></i><i></i><i></i></div><header class=\"signature\"><div class=\"signature-inner\">")
            .Append(Assets.Value.Logo).Append("</div></header><main class=\"report\">");
        html.Append("<header class=\"report-heading\"><h1>Informe estadístico</h1><p class=\"school-name\">").Append(E(data.SchoolName)).Append(" · CUE ").Append(E(data.Cue)).Append("</p><div class=\"meta\"><span>Año lectivo: <strong>").Append(E(schoolYear ?? "Todos")).Append("</strong></span><span>Informe al <strong>").Append(E(generatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))).Append("</strong></span><span>Filtros: <strong>").Append(E(FilterLabel(course, examVersionId, exams))).Append("</strong></span></div></header>");

        html.Append("<section class=\"kpis\" aria-label=\"Indicadores principales\">");
        Tile(html, "Exámenes aplicados", data.DeliveredExamCount.ToString(CultureInfo.InvariantCulture));
        Tile(html, "Intentos entregados", attempts.Count.ToString(CultureInfo.InvariantCulture));
        Tile(html, "Promedio", average.HasValue ? Percent(average.Value) : "—");
        Tile(html, "Participación", completion.HasValue ? Percent(completion.Value) : "Sin datos");
        html.Append("</section>");

        if (attempts.Count == 0)
        {
            html.Append("<section class=\"empty\"><h2>Sin intentos entregados</h2><p>Cuando se entreguen evaluaciones, este informe incluirá resultados, participación y análisis por bloque.</p></section>");
        }
        else
        {
            AppendScoreSections(html, exams, attempts);
            AppendCourseSections(html, attempts);
            AppendBlockSections(html, exams);
            AppendTimeSection(html, attempts);
            AppendAttentionSection(html, attempts, missing, hideStudentRows);
        }

        html.Append("</main><footer class=\"report-footer\"><span>Generado el ").Append(E(generatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture))).Append("</span><strong>Plan COPE · Ministerio de Educación</strong></footer></body></html>");
        return html.ToString();
    }

    private static void AppendScoreSections(StringBuilder html, IReadOnlyList<ExamStatsDto> exams, IReadOnlyList<StatsReportAttemptDto> attempts)
    {
        var perExam = attempts.GroupBy(attempt => attempt.ExamVersionId).OrderBy(group => group.First().ExamCode, StringComparer.Ordinal).ThenBy(group => group.Key, StringComparer.Ordinal).ToList();
        html.Append("<section><h2>Distribución de puntajes</h2><div class=\"cards\">");
        foreach (var group in perExam)
        {
            var scores = group.Where(attempt => attempt.ScorePercent.HasValue).Select(attempt => attempt.ScorePercent!.Value).OrderBy(score => score).ToList();
            var exam = exams.FirstOrDefault(item => item.ExamVersionId == group.Key);
            var examLabel = exam is null ? group.First().ExamCode : $"{exam.ExamCode} v{exam.VersionNumber}";
            html.Append("<article class=\"card\"><h3>").Append(E(examLabel)).Append("</h3>");
            if (scores.Count == 0)
            {
                html.Append("<p class=\"muted\">Todavía no hay puntajes corregidos para este examen.</p>");
            }
            else
            {
                html.Append(Histogram(scores)).Append("<p class=\"metrics\">Promedio <strong>").Append(Percent(scores.Average())).Append("</strong> · Mediana <strong>").Append(Percent(Median(scores))).Append("</strong> · Desvío <strong>").Append(Percent(StandardDeviation(scores))).Append("</strong></p>");
            }
            html.Append("</article>");
        }
        html.Append("</div></section>");
    }

    private static void AppendCourseSections(StringBuilder html, IReadOnlyList<StatsReportAttemptDto> attempts)
    {
        var groups = attempts.GroupBy(attempt => (attempt.Course, attempt.Section)).OrderBy(group => group.Key.Course, StringComparer.Ordinal).ThenBy(group => group.Key.Section, StringComparer.Ordinal).ToList();
        if (groups.Count == 0) return;
        html.Append("<section><h2>Comparación por curso y sección</h2><div class=\"chart-wrap\"><svg class=\"bars\" viewBox=\"0 0 720 ").Append(Math.Max(90, groups.Count * 46 + 20).ToString(CultureInfo.InvariantCulture)).Append("\" role=\"img\" aria-label=\"Promedio por curso y sección\">");
        var row = 0;
        foreach (var group in groups)
        {
            var scores = group.Where(attempt => attempt.ScorePercent.HasValue).Select(attempt => attempt.ScorePercent!.Value).ToList();
            var y = 12 + row * 46;
            html.Append("<text x=\"0\" y=\"").Append((y + 18).ToString(CultureInfo.InvariantCulture)).Append("\">").Append(E(SectionLabel(group.Key.Course, group.Key.Section))).Append("</text>");
            if (scores.Count == 0)
            {
                html.Append("<text class=\"value\" x=\"620\" y=\"").Append((y + 18).ToString(CultureInfo.InvariantCulture)).Append("\">Sin datos</text>");
            }
            else
            {
                var mean = scores.Average();
                html.Append("<rect x=\"190\" y=\"").Append(y.ToString(CultureInfo.InvariantCulture)).Append("\" width=\"").Append((Math.Clamp(mean, 0, 100) * 4).ToString("F1", CultureInfo.InvariantCulture)).Append("\" height=\"28\" rx=\"0\"/><text class=\"value\" x=\"620\" y=\"").Append((y + 18).ToString(CultureInfo.InvariantCulture)).Append("\">").Append(Percent(mean)).Append("</text>");
            }
            row++;
        }
        html.Append("</svg></div><div class=\"table-wrap\"><table><thead><tr><th>Curso</th><th>Sección</th><th>Intentos</th><th>Promedio</th></tr></thead><tbody>");
        foreach (var group in groups)
        {
            var scores = group.Where(attempt => attempt.ScorePercent.HasValue).Select(attempt => attempt.ScorePercent!.Value).ToList();
            html.Append("<tr><td>").Append(E(group.Key.Course)).Append("</td><td>").Append(E(group.Key.Section)).Append("</td><td>").Append(group.Count().ToString(CultureInfo.InvariantCulture)).Append("</td><td>").Append(scores.Count == 0 ? "Sin datos" : Percent(scores.Average())).Append("</td></tr>");
        }
        html.Append("</tbody></table></div></section>");
    }

    private static void AppendBlockSections(StringBuilder html, IReadOnlyList<ExamStatsDto> exams)
    {
        var rows = exams.SelectMany(exam => exam.Blocks.Select((block, index) => (
            Exam: exam,
            Block: block,
            QuestionNumber: (block.OrderIndex ?? index) + 1))).ToList();
        if (rows.Count == 0) return;
        var maxTotal = Math.Max(1, rows.Max(row => row.Block.CorrectCount + row.Block.PartialCount + row.Block.IncorrectCount + row.Block.BlankCount));
        html.Append("<section><h2>Dificultad por bloque</h2><div class=\"chart-wrap\"><svg class=\"stacked-bars\" viewBox=\"0 0 760 ").Append(Math.Max(80, rows.Count * 42 + 12).ToString(CultureInfo.InvariantCulture)).Append("\" role=\"img\" aria-label=\"Resultados correctos, parciales, incorrectos y en blanco por bloque\">");
        var index = 0;
        foreach (var (exam, block, questionNumber) in rows)
        {
            var y = 6 + index * 42;
            html.Append("<text x=\"0\" y=\"").Append((y + 18).ToString(CultureInfo.InvariantCulture)).Append("\">").Append(E($"{exam.ExamCode} · Pregunta {questionNumber}")).Append("</text>");
            var x = 230.0;
            foreach (var segment in new[] { (block.CorrectCount, "#356f23"), (block.PartialCount, "#f68e13"), (block.IncorrectCount, "#f4492e"), (block.BlankCount, "#8a8886") })
            {
                var width = segment.Item1 * 500.0 / maxTotal;
                if (width > 0)
                {
                    html.Append("<rect x=\"").Append(x.ToString("F1", CultureInfo.InvariantCulture)).Append("\" y=\"").Append(y.ToString(CultureInfo.InvariantCulture)).Append("\" width=\"").Append(width.ToString("F1", CultureInfo.InvariantCulture)).Append("\" height=\"26\" rx=\"0\" fill=\"").Append(segment.Item2).Append("\"><title>").Append(segment.Item1.ToString(CultureInfo.InvariantCulture)).Append("</title></rect>");
                    x += width;
                }
            }
            index++;
        }
        html.Append("</svg></div><p class=\"legend\"><span>Correctas</span><span>Parciales</span><span>Incorrectas</span><span>En blanco</span></p></section>");
        html.Append("<section><h2>Resultados por bloque</h2><div class=\"table-wrap\"><table><thead><tr><th>Examen · bloque</th><th>Correctas</th><th>Parciales</th><th>Incorrectas</th><th>En blanco</th><th>No corregibles</th></tr></thead><tbody>");
        foreach (var (exam, block, questionNumber) in rows)
        {
            html.Append("<tr><td>").Append(E($"{exam.ExamCode} v{exam.VersionNumber} · Pregunta {questionNumber}"));
            if (!string.IsNullOrWhiteSpace(block.Title)) html.Append("<small class=\"muted\"> — ").Append(E(block.Title)).Append("</small>");
            html.Append("</td><td>").Append(block.CorrectCount.ToString(CultureInfo.InvariantCulture)).Append("</td><td>").Append(block.PartialCount.ToString(CultureInfo.InvariantCulture)).Append("</td><td>").Append(block.IncorrectCount.ToString(CultureInfo.InvariantCulture)).Append("</td><td>").Append(block.BlankCount.ToString(CultureInfo.InvariantCulture)).Append("</td><td>").Append(block.UngradableCount.ToString(CultureInfo.InvariantCulture)).Append("</td></tr>");
        }
        html.Append("</tbody></table></div><p class=\"muted\">Cada fila representa los resultados agregados de un bloque.</p></section>");
    }

    private static void AppendTimeSection(StringBuilder html, IReadOnlyList<StatsReportAttemptDto> attempts)
    {
        var durations = attempts.Where(attempt => attempt.StartedAt.HasValue && attempt.SubmittedAt.HasValue)
            .Select(attempt => (attempt.SubmittedAt!.Value - attempt.StartedAt!.Value).TotalMinutes)
            .Where(minutes => minutes >= 0 && minutes <= 24 * 60).OrderBy(minutes => minutes).ToList();
        if (durations.Count == 0) return;
        var bins = new int[5];
        foreach (var duration in durations) bins[Math.Min((int)(duration / 20), bins.Length - 1)]++;
        html.Append("<section><h2>Tiempo de resolución</h2><div class=\"distribution\">");
        var labels = new[] { "0–20 min", "20–40 min", "40–60 min", "60–80 min", "80+ min" };
        for (var i = 0; i < bins.Length; i++)
        {
            var width = Math.Max(2, bins[i] * 100.0 / durations.Count);
            html.Append("<div class=\"dist-row\"><span>").Append(labels[i]).Append("</span><div><i style=\"width:").Append(width.ToString("F1", CultureInfo.InvariantCulture)).Append("%\"></i></div><strong>").Append(bins[i].ToString(CultureInfo.InvariantCulture)).Append("</strong></div>");
        }
        html.Append("</div></section>");
    }

    private static void AppendAttentionSection(StringBuilder html, IReadOnlyList<StatsReportAttemptDto> attempts, int missing, bool hideStudentRows)
    {
        html.Append("<section><h2>Seguimiento</h2>");
        if (missing > 0) html.Append("<p><strong>").Append(missing.ToString(CultureInfo.InvariantCulture)).Append(" estudiantes sin entrega</strong> según la cantidad esperada en las sesiones.</p>");
        var lowest = attempts.Where(attempt => attempt.ScorePercent.HasValue).OrderBy(attempt => attempt.ScorePercent).Take(10).ToList();
        if (lowest.Count == 0) html.Append("<p class=\"muted\">Aún no hay puntajes corregidos que requieran seguimiento.</p>");
        else if (hideStudentRows) html.Append("<p class=\"muted\">").Append(E(SuppressibleValue<int>.SuppressionLabel)).Append(". El detalle de estudiantes está oculto para este grupo.</p>");
        else
        {
            html.Append("<div class=\"table-wrap\"><table><thead><tr><th>Estudiante</th><th>Documento · últimos 4</th><th>Examen</th><th>Puntaje</th></tr></thead><tbody>");
            foreach (var attempt in lowest)
                html.Append("<tr><td>").Append(E(string.IsNullOrWhiteSpace(attempt.StudentName) ? "Sin nombre registrado" : attempt.StudentName)).Append("</td><td>").Append(E(attempt.DocumentLast4 ?? "—")).Append("</td><td>").Append(E(attempt.ExamCode)).Append("</td><td>").Append(Percent(attempt.ScorePercent!.Value)).Append("</td></tr>");
            html.Append("</tbody></table></div>");
        }
        html.Append("</section>");
    }

    private static string Histogram(IReadOnlyList<double> scores)
    {
        var bins = new int[5];
        foreach (var score in scores) bins[Math.Min(4, Math.Max(0, (int)(Math.Clamp(score, 0, 100) / 20)))]++;
        var max = Math.Max(1, bins.Max());
        var svg = new StringBuilder("<svg class=\"histogram\" viewBox=\"0 0 500 155\" role=\"img\" aria-label=\"Distribución de puntajes\">");
        for (var i = 0; i < bins.Length; i++)
        {
            var height = bins[i] * 100.0 / max;
            var x = 30 + i * 94;
            svg.Append("<rect x=\"").Append(x.ToString(CultureInfo.InvariantCulture)).Append("\" y=\"").Append((112 - height).ToString("F1", CultureInfo.InvariantCulture)).Append("\" width=\"52\" height=\"").Append(height.ToString("F1", CultureInfo.InvariantCulture)).Append("\" rx=\"0\"/><text x=\"").Append((x - 1).ToString(CultureInfo.InvariantCulture)).Append("\" y=\"137\">").Append((i * 20).ToString(CultureInfo.InvariantCulture)).Append("–").Append(((i + 1) * 20).ToString(CultureInfo.InvariantCulture)).Append("%</text>");
        }
        return svg.Append("</svg>").ToString();
    }

    private static double Median(IReadOnlyList<double> sorted) => sorted.Count % 2 == 0 ? (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2 : sorted[sorted.Count / 2];
    private static double StandardDeviation(IReadOnlyList<double> values) => Math.Sqrt(values.Sum(value => Math.Pow(value - values.Average(), 2)) / values.Count);
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string Percent(double value) => value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
    private static string FilterLabel(string? course, string? examId, IReadOnlyList<ExamStatsDto> exams) => string.Join(" · ", new[] { string.IsNullOrWhiteSpace(course) ? "Todos los cursos" : $"Curso {course}", string.IsNullOrWhiteSpace(examId) ? "Todos los exámenes" : exams.FirstOrDefault(exam => exam.ExamVersionId == examId) is { } exam ? $"Examen {exam.ExamCode} v{exam.VersionNumber}" : "Examen seleccionado" });
    private static string SectionLabel(string course, string section) => string.IsNullOrWhiteSpace(section) ? $"Curso {course}" : $"Curso {course} · {section}";
    private static void Tile(StringBuilder html, string label, string value) => html.Append("<article><p>").Append(E(label)).Append("</p><strong>").Append(E(value)).Append("</strong></article>");

    private static string BuildStyles() => string.Concat(
        "@font-face{font-family:Barlow;src:url(data:font/woff2;base64,", Assets.Value.Barlow400, ") format('woff2');font-weight:400;font-display:swap}",
        "@font-face{font-family:Barlow;src:url(data:font/woff2;base64,", Assets.Value.Barlow700, ") format('woff2');font-weight:700;font-display:swap}",
        "@font-face{font-family:'Barlow Semi Condensed';src:url(data:font/woff2;base64,", Assets.Value.Condensed800, ") format('woff2');font-weight:800;font-display:swap}", Styles);

    private static ReportAssets LoadAssets() => new(
        ReadBinaryResource("PlanCope.Local.Api.ReportFonts.barlow-400.woff2"),
        ReadBinaryResource("PlanCope.Local.Api.ReportFonts.barlow-700.woff2"),
        ReadBinaryResource("PlanCope.Local.Api.ReportFonts.barlow-condensed-800.woff2"),
        ReadTextResource("PlanCope.Local.Api.ReportAssets.logo-educacion-h.svg"));

    private static string ReadBinaryResource(string name)
    {
        using var stream = typeof(StatsHtmlReportBuilder).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded report asset: {name}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Convert.ToBase64String(memory.ToArray());
    }

    private static string ReadTextResource(string name)
    {
        using var stream = typeof(StatsHtmlReportBuilder).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing embedded report asset: {name}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ReportAssets(string Barlow400, string Barlow700, string Condensed800, string Logo);

    private const string Styles = """
        *{box-sizing:border-box}body{margin:0;background:#fff;color:#2e2d2c;font:16px/1.45 Barlow,system-ui,sans-serif;font-variant-numeric:tabular-nums}.ribbon{height:8px;display:flex}.ribbon i{flex:1}.ribbon i:nth-child(1){background:#6f0603}.ribbon i:nth-child(2){background:#ea2f09}.ribbon i:nth-child(3){background:#faae05}.ribbon i:nth-child(4){background:#facd05}.ribbon i:nth-child(5){background:#769fd3}.signature{background:#fff;border-bottom:1px solid #e0dfde}.signature-inner{max-width:1120px;margin:0 auto;padding:12px 28px}.signature svg{display:block;width:auto;height:60px;max-width:100%}.report{max-width:1120px;margin:0 auto;padding:28px}.report-heading{border-top:6px solid #facd05;padding-top:16px}.eyebrow{margin:0;color:#5f5e5c;font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:.06em}.school-name{font-size:1.15rem;font-weight:700;margin:.5rem 0}h1,h2,h3{color:#2e2d2c}h1{font:800 2.5rem/.96 'Barlow Semi Condensed',Barlow,sans-serif;text-transform:uppercase;margin:.3rem 0 .7rem}h2{font:800 1.6rem/1.05 'Barlow Semi Condensed',Barlow,sans-serif;text-transform:uppercase;margin:0 0 16px}h3{font:700 1.2rem Barlow,sans-serif;margin:0 0 12px}.muted{color:#5f5e5c}.meta{display:flex;flex-wrap:wrap;gap:8px 28px;margin-top:20px;padding:12px;background:#ededed}.kpis{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:20px 0 36px}.kpis article,.card{background:#fff;border:1px solid #e0dfde;border-radius:4px;padding:16px}.kpis p{margin:0 0 4px;color:#5f5e5c}.kpis strong{font-size:1.5rem}section{margin:32px 0}.cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:12px}.histogram{width:100%;height:auto;max-height:180px}.histogram rect,.bars rect,.dist-row i{fill:#356f23;background:#356f23}.histogram text,.bars text,.stacked-bars text{font:12px Barlow,system-ui,sans-serif;fill:#2e2d2c}.chart-wrap,.table-wrap{overflow-x:auto}.bars{width:100%;min-width:650px;max-height:460px}.stacked-bars{width:100%;min-width:700px;max-height:650px}.bars .value{font-weight:700}.legend{display:flex;flex-wrap:wrap;gap:18px;color:#2e2d2c;font-size:.9rem}.legend span:before{content:'';display:inline-block;width:10px;height:10px;border-radius:2px;background:#356f23;margin-right:6px}.legend span:nth-child(2):before{background:#f68e13}.legend span:nth-child(3):before{background:#f4492e}.legend span:nth-child(4):before{background:#8a8886}.table-wrap{border:1px solid #e0dfde;border-radius:4px;background:#fff}table{border-collapse:collapse;width:100%;font-size:.94rem}th,td{text-align:left;padding:10px 14px;border-bottom:1px solid #ededed}th{background:#f5f5f4;color:#5f5e5c;font-size:13px;text-transform:uppercase;letter-spacing:.06em}tbody tr:last-child td{border-bottom:0}.distribution{max-width:700px}.dist-row{display:grid;grid-template-columns:100px 1fr 35px;align-items:center;gap:12px;margin:10px 0}.dist-row div{height:12px;background:#ededed;border-radius:2px;overflow:hidden}.dist-row i{display:block;height:100%;border-radius:0}.dist-row strong{text-align:right}.empty{padding:20px;background:#fff;border:1px solid #e0dfde;border-radius:4px}.report-footer{background:#2e2d2c;color:#fff;display:flex;justify-content:space-between;gap:16px;padding:18px max(28px,calc((100% - 1064px)/2));font-size:14px}@media(max-width:700px){.report{padding:16px}.signature-inner{padding:10px 16px}.signature svg{height:auto;width:100%;max-height:52px}.kpis{grid-template-columns:repeat(2,1fr)}.meta{display:grid;gap:8px}.report-footer{display:grid;padding:16px}}@media print{body{background:#fff;color:#111;font-size:10pt}.ribbon,.signature{print-color-adjust:exact;-webkit-print-color-adjust:exact}.report{max-width:none;padding:18px 0}.report-heading{break-after:avoid}.report-footer{background:#fff;color:#2e2d2c;border-top:1px solid #999;margin-top:22px;padding:12px 0}.kpis{gap:8px;margin:12px 0 22px}.kpis article,.card{break-inside:avoid;border-color:#aaa;padding:10px}section{margin:22px 0;break-inside:avoid}h1{font-size:22pt}h2{font-size:14pt}th,td{padding:6px 8px}.cards{grid-template-columns:1fr 1fr}}
        """;

}
