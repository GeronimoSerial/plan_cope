import { useEffect, useMemo, useRef, useState } from "react";
import { ApiClient, type CourseStatDto, type ExamStatDto, type StatsFilterOptionsDto } from "../api/apiClient";
import { downloadBlob, openStatsReport } from "../hostBridge";
import { SearchableCombobox } from "../../shared/ui";

type StatsWorkspaceProps = {
  apiBaseUrl: string;
  cue: string;
  schoolYear?: string | null;
};

const refreshIntervalMs = 15000;

function displayAttemptCount(value: number | string): string {
  return typeof value === "string" ? value : String(value);
}

function displayAverageScorePercent(value: number | string): string {
  return typeof value === "string" ? value : value.toFixed(1);
}

function displayCourse(value: string): string {
  return value === "sin_asignar" ? "Sin curso asignado" : value;
}

function formatElapsedSeconds(updatedAt: number, now: number): number {
  return Math.max(0, Math.floor((now - updatedAt) / 1000));
}

export function StatsWorkspace({ apiBaseUrl, cue, schoolYear }: StatsWorkspaceProps) {
  const api = useMemo(() => new ApiClient(apiBaseUrl), [apiBaseUrl]);
  const [activeCue, setActiveCue] = useState(cue);
  const [schools, setSchools] = useState<{ code: string; name: string }[]>([]);
  const [schoolYearFilter, setSchoolYearFilter] = useState(schoolYear ?? "");
  const [courseFilter, setCourseFilter] = useState("");
  const [examFilter, setExamFilter] = useState("");
  const [filterOptions, setFilterOptions] = useState<StatsFilterOptionsDto>({ schoolYears: [], courses: [], exams: [] });
  const [courseStats, setCourseStats] = useState<CourseStatDto[]>([]);
  const [examStats, setExamStats] = useState<ExamStatDto[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [manualRefreshKey, setManualRefreshKey] = useState(0);
  const [error, setError] = useState<string | null>(null);
  const [reportStatus, setReportStatus] = useState<string | null>(null);
  const [reportError, setReportError] = useState<string | null>(null);
  const [updatedAt, setUpdatedAt] = useState<number | null>(null);
  const [now, setNow] = useState(Date.now());
  const hasLoadedStats = useRef(false);

  useEffect(() => { setActiveCue(cue); }, [cue]);

  useEffect(() => {
    const controller = new AbortController();
    void api.getSchools(controller.signal).then(setSchools).catch(() => undefined);
    return () => controller.abort();
  }, [api]);

  useEffect(() => {
    if (schoolYear && schoolYear !== schoolYearFilter) {
      setIsLoading(true);
      setSchoolYearFilter(schoolYear);
    }
  }, [schoolYear, schoolYearFilter]);

  useEffect(() => {
    const controller = new AbortController();
    if (!activeCue) return () => controller.abort();
    void api.getStatsFilterOptions(activeCue, controller.signal)
      .then(options => setFilterOptions(options))
      .catch(exception => {
        if (!controller.signal.aborted) {
          setError(exception instanceof Error ? exception.message : "No se pudieron recuperar los filtros.");
        }
      });
    return () => controller.abort();
  }, [api, activeCue]);

  useEffect(() => {
    let disposed = false;
    let timer: number | undefined;
    let requestController: AbortController | null = null;

    const refresh = async (manual = false) => {
      if (disposed || document.visibilityState === "hidden" || requestController) return;
      requestController = new AbortController();
      const controller = requestController;
      if (manual) setIsRefreshing(true);
      if (!hasLoadedStats.current) setIsLoading(true);
      setError(null);
      try {
        const [courses, exams] = await Promise.all([
          api.getCourseStats(activeCue, schoolYearFilter || undefined, controller.signal),
          api.getExamStats(activeCue, schoolYearFilter || undefined, courseFilter || undefined, controller.signal)
        ]);
        if (!controller.signal.aborted) {
          setCourseStats(courses);
          setExamStats(exams);
          hasLoadedStats.current = true;
          setUpdatedAt(Date.now());
        }
      } catch (exception) {
        if (!controller.signal.aborted) {
          setError(exception instanceof Error ? exception.message : "No se pudieron recuperar las estadísticas.");
        }
      } finally {
        if (requestController === controller) requestController = null;
        if (!disposed && !controller.signal.aborted) {
          setIsLoading(false);
          setIsRefreshing(false);
          timer = window.setTimeout(() => { void refresh(); }, refreshIntervalMs);
        }
      }
    };

    const onVisibilityChange = () => {
      if (document.visibilityState === "hidden") {
        if (timer !== undefined) window.clearTimeout(timer);
        timer = undefined;
        requestController?.abort();
        requestController = null;
        setIsLoading(false);
        setIsRefreshing(false);
      } else {
        if (timer !== undefined) window.clearTimeout(timer);
        timer = undefined;
        void refresh();
      }
    };

    document.addEventListener("visibilitychange", onVisibilityChange);
    void refresh(manualRefreshKey > 0);
    return () => {
      disposed = true;
      if (timer !== undefined) window.clearTimeout(timer);
      requestController?.abort();
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [api, activeCue, schoolYearFilter, courseFilter, manualRefreshKey]);

  useEffect(() => {
    if (updatedAt === null) return;
    const timer = window.setInterval(() => {
      if (document.visibilityState !== "hidden") setNow(Date.now());
    }, 1000);
    return () => window.clearInterval(timer);
  }, [updatedAt]);

  const hasAttempts = examStats.some(exam => typeof exam.attemptCount === "number" ? exam.attemptCount > 0 : Number(exam.attemptCount) > 0);

  const handleRefresh = () => {
    setIsRefreshing(true);
    setManualRefreshKey(value => value + 1);
  };

  const handleReport = async () => {
    setReportStatus(null);
    setReportError(null);
    try {
      const status = await openStatsReport(
        { cue: activeCue, schoolYear: schoolYearFilter || undefined, course: courseFilter || undefined, exam: examFilter || undefined },
        () => api.getStatsHtmlReport(activeCue, schoolYearFilter || undefined, courseFilter || undefined, examFilter || undefined)
      );
      setReportStatus(status);
    } catch (exception) {
      setReportError(exception instanceof Error ? exception.message : "No se pudo generar el informe HTML.");
    }
  };

  const handleCsvExport = async () => {
    setReportStatus(null);
    setReportError(null);
    try {
      const blob = await api.getStatsExportCsv(activeCue, schoolYearFilter || undefined);
      downloadBlob(blob, `estadisticas-${activeCue.replace(/[^a-zA-Z0-9_-]/g, "_")}.csv`);
      setReportStatus("Archivo CSV descargado.");
    } catch (exception) {
      setReportError(exception instanceof Error ? exception.message : "No se pudo descargar el archivo CSV.");
    }
  };

  return (
    <section className="panel node-workspace-panel stats-workspace-panel">
      <h2>Estadísticas</h2>
      <p className="stats-live-copy">Pantalla en vivo. El informe HTML es una captura e indica cuándo se generó.</p>

      <div className="stats-filters">
        <SearchableCombobox id="stats-school-filter" label="Escuela" value={activeCue} placeholder="Buscá por nombre o CUE"
          options={schools.map(school => ({ value: school.code, label: school.name, description: `CUE ${school.code}` }))} onChange={value => {
            hasLoadedStats.current = false;
            setSchoolYearFilter("");
            setCourseFilter("");
            setExamFilter("");
            setActiveCue(value);
          }} />
        <label htmlFor="stats-school-year-filter">Año lectivo
          <select id="stats-school-year-filter" value={schoolYearFilter} onChange={event => {
            setIsLoading(true);
            setSchoolYearFilter(event.target.value);
          }}>
            <option value="">Todos los años</option>
            {filterOptions.schoolYears.map(year => <option key={year} value={year}>{year}</option>)}
          </select>
        </label>

        <label htmlFor="stats-course-filter">Curso
          <select id="stats-course-filter" value={courseFilter} onChange={event => {
            setIsLoading(true);
            setCourseFilter(event.target.value);
          }}>
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
        <button className="button button-primary" type="button" onClick={handleReport}>Generar informe HTML</button>
        <button className="button button-secondary" type="button" onClick={() => void handleCsvExport()}>Descargar CSV</button>
        <button id="stats-refresh-button" className="button button-secondary" type="button" onClick={handleRefresh} disabled={isRefreshing}>Actualizar ahora</button>
      </div>
      {updatedAt !== null && <p className="stats-updated" role="status" aria-live="polite">Actualizado hace {formatElapsedSeconds(updatedAt, now)}s</p>}
      {reportStatus && <p className="stats-report-status" role="status" aria-live="polite">{reportStatus}</p>}
      {reportError && <p className="workspace-error" role="alert">{reportError}</p>}
      {error && <p className="workspace-error" role="alert">{error}</p>}
      {isLoading ? (
        <p role="status">{hasLoadedStats.current ? "Actualizando estadísticas…" : "Cargando estadísticas…"}</p>
      ) : !hasAttempts ? (
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
              <div className="table-wrap stats-block-table-wrap"><table className="stats-block-table">
                <thead><tr><th>Bloque</th><th>Correctas</th><th>Parciales</th><th>Incorrectas</th><th>En blanco</th><th>No corregibles</th></tr></thead>
                <tbody>{exam.blocks.map((block, index) => (
                  <tr key={block.blockId}>
                    <td><div className="stats-block-label"><strong>Pregunta {(block.orderIndex ?? index) + 1}</strong>{block.title && <span title={block.title}>{block.title}</span>}</div></td>
                    <td>{block.correctCount}</td><td>{block.partialCount}</td><td>{block.incorrectCount}</td><td>{block.blankCount}</td><td>{block.ungradableCount}</td>
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
