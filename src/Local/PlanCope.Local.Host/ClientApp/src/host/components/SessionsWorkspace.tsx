import { useEffect, useMemo, useState } from "react";
import { ApiClient } from "../api/apiClient";
import type { DeliverySessionState } from "../hooks/useDeliverySession";
import type { LocalSession, SessionHistoryPage } from "../types";
import { ActiveSessionPanel } from "./ActiveSessionPanel";
import { SessionCreatePanel } from "./SessionCreatePanel";
import { isValidCue, normalizeCueInput } from "../domain/cue";
import { ActionButton, Field, TextInput } from "../../shared/ui";

type Props = { delivery: DeliverySessionState; apiBaseUrl: string; tab: "home" | "history"; expiryPending: boolean; onStats: () => void; onReturnHome: () => void };

export function SessionsWorkspace({ delivery, apiBaseUrl, tab, expiryPending, onStats, onReturnHome }: Props) {
  const { examCatalog, sessionForm, activeSession } = delivery;
  const api = useMemo(() => new ApiClient(apiBaseUrl), [apiBaseUrl]);
  const [createStep, setCreateStep] = useState<"schools" | "manual" | "form" | null>(null);
  const [history, setHistory] = useState<SessionHistoryPage>({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  const [schoolFilter, setSchoolFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const currentSession = activeSession.session;

  useEffect(() => {
    if (tab !== "history" || currentSession) return;
    const controller = new AbortController();
    setHistoryLoading(true);
    setHistoryError(null);
    void api.getSessionHistory({ schoolCode: schoolFilter, status: statusFilter, page: history.page, pageSize: history.pageSize }, controller.signal)
      .then(setHistory).catch(error => { if (!controller.signal.aborted) setHistoryError(error instanceof Error ? error.message : "No se pudo cargar el historial."); })
      .finally(() => { if (!controller.signal.aborted) setHistoryLoading(false); });
    return () => controller.abort();
  }, [api, tab, currentSession, schoolFilter, statusFilter, history.page, history.pageSize]);

  const createPanel = <SessionCreatePanel exams={examCatalog.exams} formErrors={sessionForm.formErrors} selectedExamId={examCatalog.selectedExamId}
    isBusy={delivery.isBusy || expiryPending} isLoadingExams={examCatalog.isLoadingExams} onCreateSession={delivery.createSession}
    onRefreshExams={() => examCatalog.loadExams()} onSelectedExamChange={examCatalog.setSelectedExamId} syncPull={delivery.syncPull} roster={delivery.roster} />;

  if (currentSession) return <>
    <div className="active-session-focus"><ActiveSessionPanel progress={activeSession.progress} session={currentSession} sessionLink={activeSession.sessionLink}
      onStatusChange={activeSession.updateSessionStatus} onDiscard={activeSession.discardSession} onReturn={() => { activeSession.returnToSessions(); setCreateStep(null); onReturnHome(); }} isBusy={delivery.isBusy} /></div>
    {currentSession.status === "closed" && <div className="stats-actions"><ActionButton variant="secondary" onClick={onStats}>Ver estadísticas</ActionButton></div>}
  </>;

  if (tab === "history") return <section className="panel node-workspace-panel">
    <h2>Historial</h2>
    <div className="stats-filters">
      <label htmlFor="history-school">Escuela<select id="history-school" value={schoolFilter} onChange={event => { setHistory({ ...history, page: 1 }); setSchoolFilter(event.target.value); }}>
        <option value="">Todas las escuelas</option>{delivery.activeSession.schools.map(school => <option key={school.code} value={school.code}>{school.name} · {school.code}</option>)}
      </select></label>
      <label htmlFor="history-status">Estado<select id="history-status" value={statusFilter} onChange={event => { setHistory({ ...history, page: 1 }); setStatusFilter(event.target.value); }}>
        <option value="">Todos</option><option value="active">Abierta</option><option value="paused">Pausada</option><option value="closed">Cerrada</option>
      </select></label>
    </div>
    {historyError && <p role="alert" className="error-banner">{historyError}</p>}
    {historyLoading ? <p role="status">Cargando historial…</p> : history.items.length ? <div className="student-table-wrap"><table className="student-table"><thead><tr><th>Fecha y horario</th><th>Escuela</th><th>Evaluación</th><th>Estado</th><th>Acceso</th><th>Entregaron</th><th></th></tr></thead><tbody>
      {history.items.map(item => <tr key={item.id}><td>{formatDate(item.startAt)} · {formatTime(item.startAt)}–{item.endAt ? formatTime(item.endAt) : "En curso"}<small>{duration(item.startAt, item.endAt)}</small></td>
        <td>{item.schoolName}<small>{item.gradeLabel || `CUE ${item.schoolCode}`}</small></td><td>{item.examTitle}</td><td>{statusLabel(item.status)}</td><td>{item.accessCode}</td>
        <td>{item.submittedCount ?? 0}/{item.expectedStudentCount}</td><td><button type="button" className="button button-secondary" onClick={() => activeSession.selectSession(item)}>{item.status === "closed" ? "Ver" : "Abrir"}</button></td></tr>)}
    </tbody></table></div> : <p>No hay sesiones para estos filtros.</p>}
    <div className="stats-actions"><button type="button" className="button button-secondary" disabled={history.page <= 1 || historyLoading} onClick={() => setHistory({ ...history, page: history.page - 1 })}>Anterior</button>
      <span>Página {history.page} · {history.totalCount} sesiones</span><button type="button" className="button button-secondary" disabled={history.page * history.pageSize >= history.totalCount || historyLoading} onClick={() => setHistory({ ...history, page: history.page + 1 })}>Siguiente</button></div>
  </section>;

  if (createStep === "form") return <><div className="stats-actions"><ActionButton variant="secondary" onClick={() => setCreateStep("schools")}>Volver</ActionButton><strong>{sessionForm.schoolName || `CUE ${sessionForm.form.cue}`}</strong></div>{expiryPending && <p className="sync-warning" role="status">La revalidación está vencida. No se puede iniciar una sesión.</p>}{createPanel}</>;
  if (createStep === "manual") return <section className="panel node-workspace-panel"><h2>Ingresar otro CUE</h2><p>Ingresá el CUE de una escuela con padrón disponible en este equipo.</p>
    <Field label="CUE" error={sessionForm.form.cue && !isValidCue(sessionForm.form.cue) ? "El CUE debe tener 9 dígitos." : undefined}><TextInput value={sessionForm.form.cue} inputMode="numeric" maxLength={9} onChange={value => sessionForm.updateForm("cue", normalizeCueInput(value))} /></Field>
    {delivery.roster.isLoading && <p role="status">Buscando el padrón…</p>}{delivery.roster.error && <p role="alert" className="error-banner">{delivery.roster.error}</p>}
    {isValidCue(sessionForm.form.cue) && !delivery.roster.isLoading && <p role="status">{delivery.roster.snapshot?.status.toLowerCase() === "ready" && delivery.roster.sections.length ? sessionForm.schoolName || `CUE ${sessionForm.form.cue}` : "Padrón no disponible en este equipo."}</p>}
    <div className="stats-actions"><ActionButton variant="secondary" onClick={() => { sessionForm.updateForm("cue", ""); setCreateStep("schools"); }}>Cancelar</ActionButton><ActionButton disabled={!isValidCue(sessionForm.form.cue) || delivery.roster.isLoading || !delivery.roster.sections.length || expiryPending} onClick={() => setCreateStep("form")}>Continuar</ActionButton></div></section>;
  if (createStep === "schools") return <section className="panel node-workspace-panel"><h2>Nueva sesión</h2><p>Elegí una escuela con padrón disponible en este equipo.</p><div className="session-list" aria-label="Escuelas disponibles">{delivery.activeSession.schools.filter(school => school.hasReadyRoster).map(school => <button key={school.code} type="button" onClick={() => { sessionForm.updateForm("cue", school.code); setCreateStep("form"); }}><strong>{school.name}</strong><span>CUE {school.code}</span></button>)}</div>
    {!delivery.activeSession.schools.some(school => school.hasReadyRoster) && <p>No hay escuelas con padrón disponible.</p>}<div className="stats-actions"><ActionButton variant="secondary" onClick={() => setCreateStep("manual")}>Ingresar otro CUE</ActionButton><ActionButton variant="secondary" onClick={() => setCreateStep(null)}>Cancelar</ActionButton></div></section>;

  return <>
    {delivery.error && <p className="error-banner workspace-error" role="alert">{delivery.error}</p>}
    <section className="panel node-workspace-panel"><div className="stats-actions"><h2>Sesiones abiertas</h2><ActionButton disabled={expiryPending} onClick={() => setCreateStep("schools")}>Nueva sesión</ActionButton></div>
      {expiryPending && <p className="sync-warning" role="status">La revalidación está vencida. Finalizá y enviá la evaluación en curso; no inicies otra sesión.</p>}
      {activeSession.activeSessions.length ? <div className="session-list node-session-list">{activeSession.activeSessions.map(session => <SessionCard key={session.id} session={session} onOpen={() => activeSession.selectSession(session)} />)}</div> : <p>No hay sesiones abiertas en este equipo.</p>}
    </section>
  </>;
}

function SessionCard({ session, onOpen }: { session: LocalSession; onOpen: () => void }) {
  return <article className="node-session-card"><div><strong>{session.schoolName || `CUE ${session.schoolCode}`}</strong><span>{session.gradeLabel || session.classroomCode || ""} · {session.examTitle || session.examVersionId}</span><span>{formatTime(session.startAt)} · {statusLabel(session.status)} · Entregaron {session.submittedCount ?? 0}/{session.expectedStudentCount}</span></div><button type="button" className="button button-secondary" onClick={onOpen}>Ver</button></article>;
}
function statusLabel(status: string) { return status === "paused" ? "Pausada" : status === "closed" ? "Cerrada" : "Abierta"; }
function formatDate(value: string) { return new Date(value).toLocaleDateString("es-AR"); }
function formatTime(value: string) { return new Date(value).toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" }); }
function duration(start: string, end?: string | null) { if (!end) return "En curso"; const minutes = Math.max(0, Math.round((Date.parse(end) - Date.parse(start)) / 60000)); return `${Math.floor(minutes / 60)} h ${minutes % 60} min`; }
