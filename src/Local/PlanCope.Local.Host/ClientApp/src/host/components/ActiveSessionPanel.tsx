import { useEffect, useMemo, useRef, useState } from "react";
import type { LocalSession, SessionProgress, SessionStudentProgress } from "../types";
import { postHostMessage } from "../bridge/nativeBridge";
import { ActionButton, Field, SectionTitle, TextInput } from "../../shared/ui";

type ActiveSessionPanelProps = {
  progress: SessionProgress | null;
  session: LocalSession | null;
  sessionLink: string;
};

type StudentFilter = "all" | "missing" | "inProgress" | "submitted";

const STATUS_LABELS: Record<SessionStudentProgress["status"], string> = {
  not_started: "No empezó",
  in_progress: "Rindiendo",
  submitted: "Entregó"
};

function formatSubmissionTime(value: string | null): string {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "—" : new Intl.DateTimeFormat("es-AR", { hour: "2-digit", minute: "2-digit" }).format(date);
}

function ActiveSessionContent({ progress, session, sessionLink }: ActiveSessionPanelProps & { session: LocalSession }) {
  const [filter, setFilter] = useState<StudentFilter>("all");
  const [highlightedIds, setHighlightedIds] = useState<string[]>([]);
  const previousStatuses = useRef<Map<string, string> | null>(null);
  const students = progress?.students ?? [];
  const nominal = students.some(student => student.status === "not_started") || Boolean(session.rosterSnapshotId || session.rosterSectionId);
  const submitted = progress?.submittedCount ?? students.filter(student => student.status === "submitted").length;
  const expected = progress?.expectedStudentCount ?? session.expectedStudentCount;
  const inProgress = progress?.inProgressCount ?? students.filter(student => student.status === "in_progress").length;
  const missing = nominal ? students.filter(student => student.status === "not_started").length : 0;
  const completion = progress?.completionPercentage ?? 0;

  useEffect(() => {
    const currentStatuses = new Map(students.map(student => [student.id, student.status]));
    if (previousStatuses.current) {
      const newlySubmitted = students
        .filter(student => student.status === "submitted" && previousStatuses.current?.get(student.id) !== "submitted")
        .map(student => student.id);
      if (newlySubmitted.length) {
        setHighlightedIds(newlySubmitted);
      }
    }
    previousStatuses.current = currentStatuses;
  }, [students]);

  useEffect(() => {
    if (!highlightedIds.length) return;
    const timeout = window.setTimeout(() => setHighlightedIds([]), 1800);
    return () => window.clearTimeout(timeout);
  }, [highlightedIds]);

  const sortedStudents = useMemo(() => [...students].sort((left, right) => {
    const rank = (student: SessionStudentProgress) => student.status === "not_started" ? 0 : student.status === "in_progress" ? 1 : 2;
    return rank(left) - rank(right) || left.displayName.localeCompare(right.displayName, "es");
  }), [students]);
  const visibleStudents = sortedStudents.filter(student => {
    if (filter === "missing") return student.status === "not_started";
    if (filter === "inProgress") return student.status === "in_progress";
    if (filter === "submitted") return student.status === "submitted";
    return true;
  });

  const filterOptions: { id: StudentFilter; label: string; count: number }[] = [
    { id: "all", label: "Todos", count: students.length },
    ...(nominal ? [{ id: "missing" as const, label: "Faltan", count: missing }] : []),
    { id: "inProgress", label: "Rindiendo", count: inProgress },
    { id: "submitted", label: "Entregaron", count: submitted }
  ];

  return (
    <aside className="panel session-panel">
      <div className="session-heading">
        <SectionTitle title="Sesión activa" description="Compartí el código o el enlace con los estudiantes." />
        {progress?.gradeLabel && <p className="session-grade-label">{progress.gradeLabel}</p>}
      </div>

      <div className="session-code session-code-active">
        <span>Código de sesión</span>
        <strong>{session.accessCode}</strong>
      </div>

      <Field label="Enlace para estudiantes">
        <TextInput value={sessionLink} readOnly placeholder="Se genera al crear la sesión" onChange={() => undefined} />
      </Field>

      <div className="progress-summary" aria-live="polite" aria-atomic="true">
        <div><span>Entregaron</span><strong>{submitted} / {expected}</strong></div>
        <div><span>Rindiendo</span><strong>{inProgress}</strong></div>
        {nominal && <div><span>Faltan</span><strong>{missing}</strong></div>}
      </div>

      <div className="progress-bar" role="progressbar" aria-label="Progreso de la sesión" aria-valuemin={0} aria-valuemax={100} aria-valuenow={completion} aria-valuetext={`${completion}% completado`}>
        <span style={{ width: `${completion}%` }} />
      </div>
      <p className="progress-label">{completion}% completado</p>

      <section className="session-students" aria-label="Estado de estudiantes">
        <h3>Estudiantes</h3>
        {!nominal && <p className="session-roster-note">Sesión sin padrón: se muestran solo quienes ingresaron.</p>}
        <div className="student-filters" aria-label="Filtrar estudiantes">
          {filterOptions.map(option => (
            <button key={option.id} type="button" className="student-filter" aria-pressed={filter === option.id} onClick={() => setFilter(option.id)}>
              {option.label} <span>{option.count}</span>
            </button>
          ))}
        </div>
        {visibleStudents.length ? (
          <div className="student-table-wrap">
            <table className="student-table">
              <thead><tr><th scope="col">Nombre</th><th scope="col">DNI</th><th scope="col">Estado</th><th scope="col">Entregó</th></tr></thead>
              <tbody>
                {visibleStudents.map(student => (
                  <tr key={student.id} className={highlightedIds.includes(student.id) ? "student-row-submitted" : undefined}>
                    <td data-label="Nombre">{student.displayName}</td>
                    <td data-label="DNI">{student.maskedDocument ?? "—"}</td>
                    <td data-label="Estado"><span className={`student-status student-status-${student.status}`}>{STATUS_LABELS[student.status]}</span></td>
                    <td data-label="Entregó">{student.status === "submitted" ? formatSubmissionTime(student.submittedAt) : "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : <p className="student-empty">No hay estudiantes para este filtro.</p>}
      </section>

      <ActionButton variant="secondary" onClick={() => postHostMessage({ type: "host:openStudentView", accessCode: session.accessCode })}>
        Abrir vista del estudiante
      </ActionButton>
    </aside>
  );
}

export function ActiveSessionPanel(props: ActiveSessionPanelProps) {
  if (!props.session) return null;
  return <ActiveSessionContent {...props} session={props.session} />;
}
