import { useEffect, useMemo, useState } from "react";
import { ApiClient } from "../api/apiClient";
import type { DeliverySessionState } from "../hooks/useDeliverySession";
import type { LocalSession, SessionHistoryPage } from "../types";
import { ActiveSessionPanel } from "./ActiveSessionPanel";
import { SessionCreatePanel } from "./SessionCreatePanel";
import { isValidCue, normalizeCueInput } from "../domain/cue";
import { ActionButton, Badge, Field, SearchableCombobox, TextInput } from "../../shared/ui";
import { GradeSectionPicker } from "../../shared/GradeSectionPicker";

type Props = { delivery: DeliverySessionState; apiBaseUrl: string; tab: "home" | "history"; expiryPending: boolean; onStats: () => void; onReturnHome: () => void };

export function SessionsWorkspace({ delivery, apiBaseUrl, tab, expiryPending, onStats, onReturnHome }: Props) {
  const { examCatalog, sessionForm, activeSession } = delivery;
  const api = useMemo(() => new ApiClient(apiBaseUrl), [apiBaseUrl]);
  const [createStep, setCreateStep] = useState<"schools" | "manual" | "form" | null>(null);
  const [history, setHistory] = useState<SessionHistoryPage>({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  const [schoolFilter, setSchoolFilter] = useState("");
  const [historySearch, setHistorySearch] = useState("");
  const [historyQuery, setHistoryQuery] = useState("");
  const [selectedSchool, setSelectedSchool] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [historySections, setHistorySections] = useState<{ course: string; division: string; shift?: string | null }[]>([]);
  const [gradeFilter, setGradeFilter] = useState("");
  const [divisionFilter, setDivisionFilter] = useState("");
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [extraStudentError, setExtraStudentError] = useState<string | null>(null);
  const [extraStudentBusy, setExtraStudentBusy] = useState(false);
  const currentSession = activeSession.session;
  const readySchools = delivery.activeSession.schools.filter(school => school.hasReadyRoster);
  const schoolOptions = delivery.activeSession.schools.map(school => ({ value: school.code, label: school.name, description: `CUE ${school.code}` }));
  useEffect(() => { if (createStep === "schools" && readySchools.length === 1) setSelectedSchool(readySchools[0].code); }, [createStep, readySchools.length]);
  useEffect(() => {
    const timeout = window.setTimeout(() => setHistoryQuery(historySearch.trim()), 300);
    return () => window.clearTimeout(timeout);
  }, [historySearch]);
  useEffect(() => {
    const controller = new AbortController();
    void api.getSessionHistoryFilters(controller.signal).then(setHistorySections).catch(() => undefined);
    return () => controller.abort();
  }, [api]);
  const addExtraStudent = async (request: { document: string; firstName: string; lastName: string }) => {
    if (!currentSession) return;
    setExtraStudentBusy(true);
    setExtraStudentError(null);
    try {
      await api.addExtraStudent(currentSession.id, request);
      await activeSession.refreshProgress();
    } catch (exception) {
      setExtraStudentError(exception instanceof Error ? exception.message : "No se pudo agregar al estudiante.");
      throw exception;
    } finally { setExtraStudentBusy(false); }
  };
  const removeExtraStudent = async (studentId: string) => {
    if (!currentSession) return;
    setExtraStudentBusy(true);
    setExtraStudentError(null);
    try {
      await api.removeExtraStudent(currentSession.id, studentId);
      await activeSession.refreshProgress();
    } catch (exception) {
      setExtraStudentError(exception instanceof Error ? exception.message : "No se pudo quitar al estudiante.");
      throw exception;
    } finally { setExtraStudentBusy(false); }
  };

  useEffect(() => {
    if (tab !== "history" || currentSession) return;
    const controller = new AbortController();
    setHistoryLoading(true);
    setHistoryError(null);
    void api.getSessionHistory({ schoolCode: schoolFilter, status: statusFilter, course: gradeFilter, division: divisionFilter, q: historyQuery, page: history.page, pageSize: history.pageSize }, controller.signal)
      .then(result => { if (!controller.signal.aborted) setHistory(result); }).catch(error => { if (!controller.signal.aborted) setHistoryError(error instanceof Error ? error.message : "No se pudo cargar el historial."); })
      .finally(() => { if (!controller.signal.aborted) setHistoryLoading(false); });
    return () => controller.abort();
  }, [api, tab, currentSession, schoolFilter, statusFilter, gradeFilter, divisionFilter, historyQuery, history.page, history.pageSize]);

  const createPanel = <SessionCreatePanel exams={examCatalog.exams} formErrors={sessionForm.formErrors} selectedExamId={examCatalog.selectedExamId}
    isBusy={delivery.isBusy || expiryPending} isLoadingExams={examCatalog.isLoadingExams} onCreateSession={delivery.createSession}
    onRefreshExams={() => examCatalog.loadExams()} onSelectedExamChange={examCatalog.setSelectedExamId} syncPull={delivery.syncPull} roster={delivery.roster} />;

  if (currentSession) return <>
    <div className="active-session-focus"><ActiveSessionPanel progress={activeSession.progress} session={currentSession} sessionLink={activeSession.sessionLink}
      onStatusChange={activeSession.updateSessionStatus} onDiscard={activeSession.discardSession} onReturn={() => { activeSession.returnToSessions(); setCreateStep(null); onReturnHome(); }}
      onStats={onStats}
      onAddExtraStudent={addExtraStudent} onRemoveExtraStudent={removeExtraStudent} extraStudentError={extraStudentError}
      isBusy={delivery.isBusy || extraStudentBusy} /></div>
  </>;

  if (tab === "history") return <section className="panel node-workspace-panel">
    <h2>Historial</h2>
    <div className="stats-filters">
      <SearchableCombobox id="history-school" label="Escuela" placeholder="Todas las escuelas" value={schoolFilter}
        options={[{ value: "", label: "Todas las escuelas", description: "" }, ...schoolOptions]}
        onChange={value => { setHistory(current => ({ ...current, page: 1 })); setSchoolFilter(value); }} />
      <label htmlFor="history-status">Estado<select id="history-status" value={statusFilter} onChange={event => { setHistory({ ...history, page: 1 }); setStatusFilter(event.target.value); }}>
        <option value="">Todos</option><option value="active">Abierta</option><option value="paused">Pausada</option><option value="closed">Cerrada</option>
      </select></label>
      <div className="history-grade-filters"><GradeSectionPicker sections={historySections} grade={gradeFilter} section={divisionFilter}
        onGradeChange={value => { setHistory(current => ({ ...current, page: 1 })); setGradeFilter(value); }}
        onSectionChange={value => { setHistory(current => ({ ...current, page: 1 })); setDivisionFilter(value); }} filters gradeId="history-grade" sectionId="history-section" /></div>
      <div className="history-search"><label className="searchable-combobox-label" htmlFor="history-search">Buscar</label><div className="history-search-control"><input id="history-search" className="control" placeholder="Escuela, examen, código o grado" value={historySearch} onChange={event => { setHistorySearch(event.target.value); setHistory(current => ({ ...current, page: 1 })); }} />{historySearch && <button className="button button-secondary" type="button" aria-label="Limpiar búsqueda" onClick={() => { setHistorySearch(""); setHistory(current => ({ ...current, page: 1 })); }}>Limpiar</button>}</div></div>
    </div>
    {historyError && <p role="alert" className="error-banner">{historyError}</p>}
    {historyLoading && <p role="status" className={history.items.length ? "history-searching" : undefined}>{history.items.length ? "Buscando…" : "Cargando historial…"}</p>}
    {history.items.length ? <div className="student-table-wrap"><table className="student-table"><thead><tr><th>Fecha y horario</th><th>Escuela</th><th>Evaluación</th><th>Estado</th><th>Acceso</th><th>Entregaron</th><th></th></tr></thead><tbody>
      {history.items.map(item => <tr key={item.id}><td>{formatDate(item.startAt)} · {formatTime(item.startAt)}<small>{historyDuration(item.startAt, item.endAt)}</small></td>
        <td>{item.schoolName}<small>{item.gradeLabel || `CUE ${item.schoolCode}`}</small></td><td>{item.examTitle}</td><td><SessionStatusBadge status={item.status} /></td><td>{item.accessCode}</td>
        <td>{item.submittedCount ?? 0}/{item.expectedStudentCount}{(item.offRosterSubmittedCount ?? 0) > 0 && <small>+{item.offRosterSubmittedCount} fuera de padrón</small>}</td><td><button type="button" className="button button-secondary" onClick={() => activeSession.selectSession(item)}>{item.status === "closed" ? "Ver" : "Abrir"}</button></td></tr>)}
    </tbody></table></div> : !historyLoading && <p>{historyQuery ? "No hay sesiones que coincidan con la búsqueda." : "No hay sesiones para estos filtros."}</p>}
    <div className="stats-actions"><button type="button" className="button button-secondary" disabled={history.page <= 1 || historyLoading} onClick={() => setHistory({ ...history, page: history.page - 1 })}>Anterior</button>
      <span>Página {history.page} · {history.totalCount} sesiones</span><button type="button" className="button button-secondary" disabled={history.page * history.pageSize >= history.totalCount || historyLoading} onClick={() => setHistory({ ...history, page: history.page + 1 })}>Siguiente</button></div>
  </section>;

  if (createStep === "form") return <><div className="stats-actions"><ActionButton variant="secondary" onClick={() => setCreateStep("schools")}>Volver</ActionButton><strong>{sessionForm.schoolName || `CUE ${sessionForm.form.cue}`}</strong></div>{expiryPending && <p className="sync-warning" role="status">La revalidación está vencida. No se puede iniciar una sesión.</p>}{createPanel}</>;
  if (createStep === "manual") return <section className="panel node-workspace-panel"><h2>Ingresar otro CUE</h2><p>Ingresá el CUE de una escuela con padrón disponible en este equipo.</p>
    <Field label="CUE" error={sessionForm.form.cue && !isValidCue(sessionForm.form.cue) ? "El CUE debe tener 9 dígitos." : undefined}><TextInput value={sessionForm.form.cue} inputMode="numeric" maxLength={9} onChange={value => sessionForm.updateForm("cue", normalizeCueInput(value))} /></Field>
    {delivery.roster.isLoading && <p role="status">Buscando el padrón…</p>}{delivery.roster.error && <p role="alert" className="error-banner">{delivery.roster.error}</p>}
    {isValidCue(sessionForm.form.cue) && !delivery.roster.isLoading && <p role="status">{delivery.roster.snapshot?.status.toLowerCase() === "ready" && delivery.roster.sections.length ? sessionForm.schoolName || `CUE ${sessionForm.form.cue}` : "Padrón no disponible en este equipo."}</p>}
    <div className="stats-actions"><ActionButton variant="secondary" onClick={() => { sessionForm.updateForm("cue", ""); setCreateStep("schools"); }}>Cancelar</ActionButton><ActionButton disabled={!isValidCue(sessionForm.form.cue) || delivery.roster.isLoading || !delivery.roster.sections.length || expiryPending} onClick={() => setCreateStep("form")}>Continuar</ActionButton></div></section>;
  if (createStep === "schools") return <section className="panel node-workspace-panel"><h2>Nueva sesión</h2><p>Elegí una escuela con padrón disponible en este equipo.</p>
    {readySchools.length > 0 && <><SearchableCombobox id="new-session-school" label="Escuela" placeholder="Buscá por nombre o CUE" value={selectedSchool} options={readySchools.map(school => ({ value: school.code, label: school.name, description: `CUE ${school.code}` }))} onChange={value => setSelectedSchool(value)} />
      {readySchools.find(school => school.code === selectedSchool) && <p className="school-selection-confirmation"><strong>{readySchools.find(school => school.code === selectedSchool)?.name}</strong><span>CUE {selectedSchool}</span></p>}</>}
    {readySchools.length === 0 && <p>No hay escuelas con padrón disponible.</p>}<div className="stats-actions"><ActionButton variant="secondary" onClick={() => setCreateStep("manual")}>Ingresar otro CUE</ActionButton><ActionButton variant="secondary" onClick={() => setCreateStep(null)}>Cancelar</ActionButton><ActionButton disabled={!selectedSchool || expiryPending} onClick={() => { sessionForm.updateForm("cue", selectedSchool); setCreateStep("form"); }}>Continuar</ActionButton></div></section>;

  return <>
    {delivery.error && <p className="error-banner workspace-error" role="alert">{delivery.error}</p>}
    <section className="panel node-workspace-panel"><div className="stats-actions"><h2 className="page-title">Sesiones abiertas</h2><ActionButton disabled={expiryPending} onClick={() => setCreateStep("schools")}>Nueva sesión</ActionButton></div>
      {expiryPending && <p className="sync-warning" role="status">La revalidación está vencida. Finalizá y enviá la evaluación en curso; no inicies otra sesión.</p>}
      {activeSession.activeSessions.length ? <div className="session-list node-session-list">{activeSession.activeSessions.map(session => <SessionCard key={session.id} session={session} onOpen={() => activeSession.selectSession(session)} />)}</div> : <p>No hay sesiones abiertas en este equipo.</p>}
    </section>
  </>;
}

function SessionCard({ session, onOpen }: { session: LocalSession; onOpen: () => void }) {
  const grade = session.gradeLabel || session.classroomCode;
  return <article className="node-session-card"><div className="node-session-card-copy">
    <div className="node-session-card-heading"><strong>{session.schoolName || `CUE ${session.schoolCode}`}</strong><SessionStatusBadge status={session.status} /></div>
    <span>{grade && <>{grade} · </>}{session.examTitle || session.examVersionId}</span>
    <div className="node-session-meta"><span>Inicio {formatTime(session.startAt)}</span><span>Entregaron {session.submittedCount ?? 0}/{session.expectedStudentCount}{(session.offRosterSubmittedCount ?? 0) > 0 && ` +${session.offRosterSubmittedCount} fuera de padrón`}</span></div>
  </div><button type="button" className="button button-secondary" onClick={onOpen}>Ver</button></article>;
}
function statusLabel(status: string) { return status === "paused" ? "Pausada" : status === "closed" ? "Cerrada" : "Abierta"; }
function SessionStatusBadge({ status }: { status: string }) {
  const tone = status === "paused" ? "warning" : status === "closed" ? "neutral" : "info";
  return <Badge tone={tone}>{statusLabel(status)}</Badge>;
}
function formatDate(value: string) { return new Date(value).toLocaleDateString("es-AR"); }
function formatTime(value: string) { return new Date(value).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" }); }
function duration(start: string, end?: string | null) { if (!end) return "En curso"; const minutes = Math.max(0, Math.round((Date.parse(end) - Date.parse(start)) / 60000)); return `${Math.floor(minutes / 60)} h ${minutes % 60} min`; }
function historyDuration(start: string, end?: string | null) { return end ? `${duration(start, end)} · hasta ${formatTime(end)}` : "En curso"; }
