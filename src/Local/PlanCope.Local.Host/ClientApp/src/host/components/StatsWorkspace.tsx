import { useEffect, useMemo, useState } from "react";
import { ApiClient, type CourseStatDto, type ExamStatDto, type StatsFilterOptionsDto } from "../api/apiClient";

type StatsWorkspaceProps = {
  apiBaseUrl: string;
  cue: string;
  schoolYear?: string | null;
};

function displayAttemptCount(value: number | string): string {
  return typeof value === "string" ? value : String(value);
}

function displayAverageScorePercent(value: number | string): string {
  return typeof value === "string" ? value : value.toFixed(1);
}

function displayCourse(value: string): string {
  return value === "sin_asignar" ? "Sin curso asignado" : value;
}

export function StatsWorkspace({ apiBaseUrl, cue, schoolYear }: StatsWorkspaceProps) {
  const api = useMemo(() => new ApiClient(apiBaseUrl), [apiBaseUrl]);
  const [schoolYearFilter, setSchoolYearFilter] = useState(schoolYear ?? "");
  const [courseFilter, setCourseFilter] = useState("");
  const [examFilter, setExamFilter] = useState("");
  const [filterOptions, setFilterOptions] = useState<StatsFilterOptionsDto>({ schoolYears: [], courses: [], exams: [] });
  const [courseStats, setCourseStats] = useState<CourseStatDto[]>([]);
  const [examStats, setExamStats] = useState<ExamStatDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (schoolYear) setSchoolYearFilter(schoolYear);
  }, [schoolYear]);

  useEffect(() => {
    const controller = new AbortController();
    void api.getStatsFilterOptions(cue, controller.signal)
      .then(options => setFilterOptions(options))
      .catch(exception => {
        if (!controller.signal.aborted) {
          setError(exception instanceof Error ? exception.message : "No se pudieron recuperar los filtros.");
        }
      });
    return () => controller.abort();
  }, [api, cue]);

  useEffect(() => {
    const controller = new AbortController();
    setIsLoading(true);
    setError(null);

    void Promise.all([
      api.getCourseStats(cue, schoolYearFilter || undefined, controller.signal),
      api.getExamStats(cue, schoolYearFilter || undefined, courseFilter || undefined, controller.signal)
    ])
      .then(([courses, exams]) => {
        setCourseStats(courses);
        setExamStats(exams);
      })
      .catch(exception => {
        if (!controller.signal.aborted) {
          setError(exception instanceof Error ? exception.message : "No se pudieron recuperar las estadísticas.");
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setIsLoading(false);
      });

    return () => controller.abort();
  }, [api, cue, schoolYearFilter, courseFilter]);

  const reportUrl = api.getStatsHtmlReportUrl(cue, schoolYearFilter || undefined, courseFilter || undefined, examFilter || undefined);
  const hasAttempts = examStats.some(exam => typeof exam.attemptCount === "number" ? exam.attemptCount > 0 : Number(exam.attemptCount) > 0);

  return (
    <section className="panel">
      <h2>Estadísticas</h2>

      <div className="stats-filters">
        <label htmlFor="stats-school-year-filter">Año lectivo
          <select id="stats-school-year-filter" value={schoolYearFilter} onChange={event => setSchoolYearFilter(event.target.value)}>
            <option value="">Todos los años</option>
            {filterOptions.schoolYears.map(year => <option key={year} value={year}>{year}</option>)}
          </select>
        </label>

        <label htmlFor="stats-course-filter">Curso
          <select id="stats-course-filter" value={courseFilter} onChange={event => setCourseFilter(event.target.value)}>
            <option value="">Todos los cursos</option>
            {filterOptions.courses.map(course => <option key={course} value={course}>{displayCourse(course)}</option>)}
          </select>
        </label>

        <label htmlFor="stats-exam-filter">Examen
          <select id="stats-exam-filter" value={examFilter} onChange={event => setExamFilter(event.target.value)}>
            <option value="">Todos los exámenes</option>
            {filterOptions.exams.map(exam => <option key={exam.examVersionId} value={exam.examVersionId}>{exam.examCode} v{exam.versionNumber}</option>)}
          </select>
        </label>
      </div>

      <div className="stats-actions">
        <a className="button button-primary" href={reportUrl} download>Generar informe HTML</a>
        <a className="button button-secondary" href={api.getStatsExportCsvUrl(cue, schoolYearFilter || undefined)} download>Descargar CSV</a>
      </div>

      {error && <p className="workspace-error">{error}</p>}
      {isLoading && <p role="status">Cargando estadísticas…</p>}

      {!isLoading && !hasAttempts ? (
        <div className="stats-empty-state">
          <h3>Todavía no hay intentos entregados</h3>
          <p>Cuando los estudiantes entreguen evaluaciones, acá vas a encontrar los resultados por curso y examen. También podés generar un informe HTML vacío para guardar o compartir.</p>
        </div>
      ) : (
        <>
          <div className="table-wrap"><table>
            <thead><tr><th>Curso</th><th>Intentos</th><th>Promedio (%)</th></tr></thead>
            <tbody>{courseStats.map(stat => (
              <tr key={stat.course}>
                <td>{displayCourse(stat.course)}</td>
                <td>{displayAttemptCount(stat.attemptCount)}</td>
                <td>{displayAverageScorePercent(stat.averageScorePercent)}</td>
              </tr>
            ))}</tbody>
          </table></div>

          {examStats.map(exam => (
            <details key={exam.examVersionId}>
              <summary>{exam.examCode} v{exam.versionNumber} — {displayAttemptCount(exam.attemptCount)} intentos</summary>
              <div className="table-wrap"><table>
                <thead><tr><th>Bloque</th><th>Correctas</th><th>Parciales</th><th>Incorrectas</th><th>En blanco</th><th>No corregibles</th></tr></thead>
                <tbody>{exam.blocks.map(block => (
                  <tr key={block.blockId}>
                    <td>{block.blockId}</td><td>{block.correctCount}</td><td>{block.partialCount}</td><td>{block.incorrectCount}</td><td>{block.blankCount}</td><td>{block.ungradableCount}</td>
                  </tr>
                ))}</tbody>
              </table></div>
            </details>
          ))}
        </>
      )}
    </section>
  );
}
