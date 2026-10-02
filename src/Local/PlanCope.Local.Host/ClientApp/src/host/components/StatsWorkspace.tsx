import { useEffect, useMemo, useRef, useState } from "react";
import { ApiClient, type CourseStatDto, type ExamStatDto, type StatsFilterOptionsDto } from "../api/apiClient";
import { openStatsReport } from "../hostBridge";
import { SearchableCombobox } from "../../shared/ui";
import { GradeSectionPicker, sectionOptionValue } from "../../shared/GradeSectionPicker";

type StatsWorkspaceProps = {
  apiBaseUrl: string;
  cue: string;
  schoolYear?: string | null;
  onBack: () => void;
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

function formatSubmissionDate(value: string): string {
  const parts = new Intl.DateTimeFormat("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" }).formatToParts(new Date(value));
  const getPart = (type: Intl.DateTimeFormatPartTypes) => parts.find(part => part.type === type)?.value ?? "";
  return `${getPart("day")}/${getPart("month")}/${getPart("year")}`;
}

export function StatsWorkspace({ apiBaseUrl, cue, schoolYear, onBack }: StatsWorkspaceProps) {
  const api = useMemo(() => new ApiClient(apiBaseUrl), [apiBaseUrl]);
  const [activeCue, setActiveCue] = useState("");
  const [schools, setSchools] = useState<{ code: string; name: string; submittedAttemptCount: number; lastSubmittedAt: string }[]>([]);
  const [schoolsLoaded, setSchoolsLoaded] = useState(false);
  const [schoolYearFilter, setSchoolYearFilter] = useState(schoolYear ?? "");
  const [courseFilter, setCourseFilter] = useState("");
  const [sectionFilter, setSectionFilter] = useState("");
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

  useEffect(() => {
    const controller = new AbortController();
    void api.getSchoolsWithAttempts(controller.signal).then(items => {
      if (controller.signal.aborted) return;
      setSchools(items);
      setSchoolsLoaded(true);
      setActiveCue(current => items.some(item => item.code === current) ? current : items[0]?.code ?? "");
    }).catch(() => { if (!controller.signal.aborted) setSchoolsLoaded(true); });
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
    if (!activeCue) return () => undefined;

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
  const selectedStatsSection = filterOptions.sections?.find(section => sectionOptionValue(section) === sectionFilter);
  const visibleCourses = courseStats.filter(stat => !courseFilter || stat.course === courseFilter);
  const visibleExams = examStats.filter(exam => {
    if (examFilter && exam.examVersionId !== examFilter) return false;
    if (selectedStatsSection && !exam.sections?.some(section => section.course === courseFilter
      && section.division === selectedStatsSection.division && (section.shift ?? "") === (selectedStatsSection.shift ?? ""))) return false;
    return true;
  });

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

  return (
    <section className="panel node-workspace-panel stats-workspace-panel">
      <div className="workspace-heading"><div><p className="workspace-location">Inicio / Estadísticas</p><h2 tabIndex={-1}>Estadísticas</h2></div><button className="button button-secondary" type="button" onClick={onBack}>Volver a sesiones</button></div>
      <p className="stats-live-copy">Los datos se actualizan cada 15 segundos. El informe HTML indica su fecha de generación.</p>

      <div className="stats-filters" aria-label="Filtros de estadísticas">
        <SearchableCombobox id="stats-school-filter" label="Escuela" value={activeCue} placeholder="Buscá por nombre o CUE"
          options={schools.map(school => ({ value: school.code, label: school.name, description: `${school.submittedAttemptCount} entregas · última ${formatSubmissionDate(school.lastSubmittedAt)} · CUE ${school.code}` }))} onChange={value => {
            hasLoadedStats.current = false;
            setSchoolYearFilter("");
            setCourseFilter("");
            setSectionFilter("");
            setExamFilter("");
            setActiveCue(value);
          }} />
        <label htmlFor="stats-school-year-filter">Año lectivo
          <select id="stats-school-year-filter" value={schoolYearFilter} disabled={!activeCue} onChange={event => {
            setIsLoading(true);
            setSchoolYearFilter(event.target.value);
          }}>
            <option value="">Todos los años</option>
            {filterOptions.schoolYears.map(year => <option key={year} value={year}>{year}</option>)}
          </select>
        </label>

        <div className="stats-grade-filters"><GradeSectionPicker sections={filterOptions.sections ?? []} grades={filterOptions.courses}
          grade={courseFilter} section={sectionFilter} onGradeChange={value => { setIsLoading(true); setCourseFilter(value); setSectionFilter(""); }}
          onSectionChange={setSectionFilter} filters disabled={!activeCue} gradeId="stats-course-filter" sectionId="stats-section-filter" /></div>

        <label htmlFor="stats-exam-filter">Examen
          <select id="stats-exam-filter" value={examFilter} disabled={!activeCue} onChange={event => setExamFilter(event.target.value)}>
            <option value="">Todos</option>
            {filterOptions.exams.map(exam => <option key={exam.examVersionId} value={exam.examVersionId}>{exam.examCode} v{exam.versionNumber}</option>)}
          </select>
        </label>
      </div>
      {sectionFilter && <p className="stats-filter-help" role="status">Se muestran exámenes vinculados con esa sección. Los totales y los informes siguen agrupados por grado.</p>}

      <div className="stats-actions">
        <button className="button button-primary" type="button" onClick={handleReport} disabled={!activeCue}>Generar informe HTML</button>
        <button id="stats-refresh-button" className="button button-secondary" type="button" onClick={handleRefresh} disabled={isRefreshing}>Actualizar ahora</button>
      </div>
      {updatedAt !== null && <p className="stats-updated" role="status" aria-live="polite">Actualizado hace {formatElapsedSeconds(updatedAt, now)}s</p>}
      {reportStatus && <p className="stats-report-status" role="status" aria-live="polite">{reportStatus}</p>}
      {reportError && <p className="workspace-error" role="alert">{reportError}</p>}
      {error && <p className="workspace-error" role="alert">{error}</p>}
      {isLoading ? (
        <p role="status">{hasLoadedStats.current ? "Actualizando estadísticas…" : "Cargando estadísticas…"}</p>
      ) : schoolsLoaded && schools.length === 0 ? (
        <div className="stats-empty-state"><p>Todavía no hay exámenes entregados en este equipo.</p></div>
      ) : !hasAttempts ? (
        <div className="stats-empty-state">
          <h3>Todavía no hay intentos entregados</h3>
          <p>Cuando los estudiantes entreguen evaluaciones, acá vas a encontrar los resultados por curso y examen.</p>
        </div>
      ) : (
        <>
          <div className="table-wrap"><table>
            <thead><tr><th>Curso</th><th>Intentos</th><th>Promedio (%)</th></tr></thead>
            <tbody>{visibleCourses.map(stat => (
              <tr key={stat.course}>
                <td>{displayCourse(stat.course)}</td>
                <td>{displayAttemptCount(stat.attemptCount)}</td>
                <td>{displayAverageScorePercent(stat.averageScorePercent)}</td>
              </tr>
            ))}</tbody>
          </table></div>

          {visibleExams.map(exam => (
            <details key={exam.examVersionId}>
              <summary>{exam.title ? `${exam.title} · ` : ""}{exam.examCode} v{exam.versionNumber} — {displayAttemptCount(exam.attemptCount)} intentos</summary>
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
          {visibleCourses.length === 0 && visibleExams.length === 0 && <p role="status">Sin resultados para la búsqueda.</p>}
        </>
      )}
    </section>
  );
}
