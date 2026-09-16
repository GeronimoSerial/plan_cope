import { useEffect, useMemo, useState } from "react";
import { ApiClient, type CourseStatDto, type ExamStatDto } from "../api/apiClient";

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

export function StatsWorkspace({ apiBaseUrl, cue, schoolYear }: StatsWorkspaceProps) {
  const api = useMemo(() => new ApiClient(apiBaseUrl), [apiBaseUrl]);

  const [courseFilter, setCourseFilter] = useState("");
  const [courseStats, setCourseStats] = useState<CourseStatDto[]>([]);
  const [examStats, setExamStats] = useState<ExamStatDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    setIsLoading(true);
    setError(null);

    void Promise.all([
      api.getCourseStats(cue, schoolYear ?? undefined, controller.signal),
      api.getExamStats(cue, schoolYear ?? undefined, courseFilter || undefined, controller.signal)
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
        if (!controller.signal.aborted) {
          setIsLoading(false);
        }
      });

    return () => controller.abort();
  }, [api, cue, schoolYear, courseFilter]);

  return (
    <section className="panel">
      <h2>Estadísticas</h2>

      <label htmlFor="stats-course-filter">Curso</label>
      <input
        id="stats-course-filter"
        type="text"
        value={courseFilter}
        onChange={event => setCourseFilter(event.target.value)}
      />

      {error && <p className="workspace-error">{error}</p>}

      <table>
        <thead>
          <tr>
            <th>Curso</th>
            <th>Intentos</th>
            <th>Promedio (%)</th>
          </tr>
        </thead>
        <tbody>
          {courseStats.map(stat => (
            <tr key={stat.course}>
              <td>{stat.course}</td>
              <td>{displayAttemptCount(stat.attemptCount)}</td>
              <td>{displayAverageScorePercent(stat.averageScorePercent)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {examStats.map(exam => (
        <details key={exam.examVersionId}>
          <summary>
            {exam.examCode} v{exam.versionNumber} — {displayAttemptCount(exam.attemptCount)} intentos
          </summary>
          <table>
            <thead>
              <tr>
                <th>Bloque</th>
                <th>Correctas</th>
                <th>Parciales</th>
                <th>Incorrectas</th>
                <th>En blanco</th>
                <th>No corregibles</th>
              </tr>
            </thead>
            <tbody>
              {exam.blocks.map(block => (
                <tr key={block.blockId}>
                  <td>{block.blockId}</td>
                  <td>{block.correctCount}</td>
                  <td>{block.partialCount}</td>
                  <td>{block.incorrectCount}</td>
                  <td>{block.blankCount}</td>
                  <td>{block.ungradableCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </details>
      ))}

      <a href={api.getStatsExportCsvUrl(cue, schoolYear ?? undefined)} download>
        Descargar CSV
      </a>
    </section>
  );
}