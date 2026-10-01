import { useEffect, useMemo, useRef, useState } from "react";
import { ApiClient, type CourseStatDto, type ExamStatDto, type StatsFilterOptionsDto } from "../api/apiClient";
import { downloadBlob, openStatsReport } from "../hostBridge";
import { SearchableCombobox, normalizeSearch, tokenizeSearch } from "../../shared/ui";
import { GradeSectionPicker } from "../../shared/GradeSectionPicker";

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

function formatSubmissionDate(value: string): string {
  const parts = new Intl.DateTimeFormat("es-AR", { day: "2-digit", month: "2-digit", year: "numeric" }).formatToParts(new Date(value));
  const getPart = (type: Intl.DateTimeFormatPartTypes) => parts.find(part => part.type === type)?.value ?? "";
  return `${getPart("day")}/${getPart("month")}/${getPart("year")}`;
}

const gradeAliases: Record<string, string[]> = {
  "1": ["primero", "1ro", "1er"],
  "2": ["segundo", "2do"],
  "3": ["tercero", "3ro", "3er"],
  "4": ["cuarto", "4to"],
  "5": ["quinto", "5to"],
  "6": ["sexto", "6to"],
  "7": ["septimo", "7mo"],
  "8": ["octavo", "8vo"],
  "9": ["noveno", "9no"],
  "10": ["decimo", "10mo"],
  "11": ["undecimo", "11mo"],
  "12": ["duodecimo", "12mo"]
};

function searchableCourse(value: string, sections: string[] = []): string[] {
  const label = displayCourse(value);
  const grade = normalizeSearch(value);
  const aliases = gradeAliases[grade] ?? [];
  const divisionLabels = sections.flatMap(section => {
    const division = normalizeSearch(section).replace(/\s+/g, "");
    return division ? [`${grade}${division}`, `${grade} ${division}`, ...aliases.map(alias => `${alias} ${division}`)] : [];
  });
  return [value, label, ...aliases, ...divisionLabels];
}

function matchesSearch(fields: string[], tokens: string[], allowGradeOnlyDivision = false): boolean {
  const normalized = normalizeSearch(fields.join(" "));
  return tokens.every(token => {
    if (normalized.includes(token)) return true;
    const gradeAndDivision = allowGradeOnlyDivision ? token.match(/^(\d{1,2})[a-z]$/) : null;
    return gradeAndDivision ? normalized.split(" ").includes(gradeAndDivision[1]) : false;
  });
}

export function StatsWorkspace({ apiBaseUrl, cue, schoolYear }: StatsWorkspaceProps) {
  const api = useMemo(() => new ApiClient(apiBaseUrl), [apiBaseUrl]);
  const [activeCue, setActiveCue] = useState("");
  const [schools, setSchools] = useState<{ code: string; name: string; submittedAttemptCount: number; lastSubmittedAt: string }[]>([]);
  const [schoolsLoaded, setSchoolsLoaded] = useState(false);
  const [schoolYearFilter, setSchoolYearFilter] = useState(schoolYear ?? "");
  const [courseFilter, setCourseFilter] = useState("");
  const [sectionFilter, setSectionFilter] = useState("");
  const [examFilter, setExamFilter] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
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
  const searchTokens = tokenizeSearch(searchQuery.slice(0, 100));
  const visibleCourses = courseStats.filter(stat => matchesSearch(searchableCourse(stat.course, stat.sections), searchTokens, !stat.sections?.length));
  const visibleExams = examStats.filter(exam => {
    if (sectionFilter && !exam.sections?.some(section => section.course === courseFilter && section.division === sectionFilter)) return false;
    const courseFields = exam.sections?.length
      ? exam.sections.flatMap(section => searchableCourse(section.course, [section.division]))
      : (exam.courses ?? []).flatMap(course => searchableCourse(course));
    return matchesSearch([exam.title ?? "", exam.examCode, `${exam.examCode} v${exam.versionNumber}`, ...courseFields], searchTokens, !exam.sections?.length);
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
          onSectionChange={value => { setIsLoading(true); setSectionFilter(value); }} filters disabled={!activeCue} gradeId="stats-course-filter" sectionId="stats-section-filter" /></div>

        <label htmlFor="stats-exam-filter">Examen
          <select id="stats-exam-filter" value={examFilter} disabled={!activeCue} onChange={event => setExamFilter(event.target.value)}>
            <option value="">Todos los exámenes</option>
            {filterOptions.exams.map(exam => <option key={exam.examVersionId} value={exam.examVersionId}>{exam.examCode} v{exam.versionNumber}</option>)}
          </select>
        </label>
      </div>
      {sectionFilter && <p className="stats-search-help" role="status">Se muestran exámenes vinculados con esa sección. Los totales y los informes siguen agrupados por grado.</p>}

      <div className="stats-actions">
        <button className="button button-primary" type="button" onClick={handleReport} disabled={!activeCue}>Generar informe HTML</button>
        <button className="button button-secondary" type="button" onClick={() => void handleCsvExport()} disabled={!activeCue}>Descargar CSV</button>
        <button id="stats-refresh-button" className="button button-secondary" type="button" onClick={handleRefresh} disabled={isRefreshing}>Actualizar ahora</button>
      </div>
      <p className="stats-search-field"><label htmlFor="stats-search">Buscar examen o curso</label><input id="stats-search" type="search" value={searchQuery} onChange={event => setSearchQuery(event.target.value)} placeholder="Buscar examen o curso" /></p>
      <p className="stats-search-help">La búsqueda solo filtra esta pantalla; los archivos exportados siguen los filtros seleccionados.</p>
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
