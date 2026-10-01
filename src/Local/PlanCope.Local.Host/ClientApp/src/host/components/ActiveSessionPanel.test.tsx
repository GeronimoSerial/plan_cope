// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { LocalSession, SessionProgress, SessionStudentProgress } from "../types";
import { ActiveSessionPanel } from "./ActiveSessionPanel";

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });

const session: LocalSession = {
  id: "session-1", examVersionId: "exam-1", schoolCode: "123456789", classroomCode: "6", startedBy: "Docente",
  startAt: "2026-10-01T10:00:00Z", status: "active", accessCode: "ABC-123", expectedStudentCount: 2,
  rosterSnapshotId: "snapshot-1", rosterSectionId: "section-1"
};

function student(overrides: Partial<SessionStudentProgress> & Pick<SessionStudentProgress, "id" | "displayName" | "status">): SessionStudentProgress {
  return { maskedDocument: "•••123", startedAt: null, submittedAt: null, attemptId: null, submissionReason: null, offRoster: false, ...overrides };
}

function progress(overrides: Partial<SessionProgress> = {}): SessionProgress {
  return {
    sessionId: session.id, accessCode: session.accessCode, expectedStudentCount: 2, startedCount: 1, submittedCount: 0,
    inProgressCount: 1, offRosterSubmittedCount: 0, offRosterInProgressCount: 0, completionPercentage: 0,
    students: [student({ id: "missing", displayName: "Brenda Missing", status: "not_started" }), student({ id: "working", displayName: "Ana Working", status: "in_progress", maskedDocument: "•••456" })],
    gradeLabel: "6° A · Turno mañana", gradeLabelWithShift: "6° A · Turno mañana", schoolCode: "123456789", schoolName: "Escuela Norte",
    course: "6°", division: "A", shift: "Mañana", level: "Primario", ...overrides
  };
}

describe("ActiveSessionPanel", () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(() => {
    if (root) act(() => root?.unmount());
    root = undefined;
    container?.remove();
    container = undefined;
  });

  function render(progressData: SessionProgress | null, sessionData = session, callbacks: { onStatusChange?: (status: "active" | "paused" | "closed") => Promise<{ submitted: number; failed: number } | void | null>; onDiscard?: () => Promise<boolean>; onReturn?: () => void; onStats?: () => void; onAddExtraStudent?: (request: { document: string; firstName: string; lastName: string }) => Promise<void>; onRemoveExtraStudent?: (studentId: string) => Promise<void> } = {}) {
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    act(() => root?.render(<ActiveSessionPanel progress={progressData} session={sessionData} sessionLink="http://local/ABC-123" {...callbacks} />));
    return container;
  }

  function rerender(progressData: SessionProgress | null, sessionData = session) {
    act(() => root?.render(<ActiveSessionPanel progress={progressData} session={sessionData} sessionLink="http://local/ABC-123" />));
    return container!;
  }

  it("shows grade, live counters, expected students, masked DNI, and accessible table headings", () => {
    const view = render(progress());

    expect(view.textContent).toContain("6° A · Turno mañana");
    expect(view.textContent).toContain("Compartí el código o el enlace con los estudiantes.");
    expect(view.querySelector(".session-grade-label strong")?.textContent).toBe("Escuela Norte");
    expect(view.querySelector(".session-grade-label")?.textContent).toContain("CUE 123456789");
    expect(view.textContent).toContain("Entregaron0 / 2");
    expect(view.textContent).toContain("Rindiendo1");
    expect(view.textContent).toContain("Faltan1");
    expect(view.querySelector("table")?.querySelectorAll("th")).toHaveLength(4);
    expect(view.textContent).toContain("•••456");
  });

  it("keeps off-roster submissions outside the roster completion counter", () => {
    const view = render(progress({
      expectedStudentCount: 20,
      startedCount: 19,
      submittedCount: 19,
      offRosterSubmittedCount: 1,
      inProgressCount: 0,
      offRosterInProgressCount: 0,
      completionPercentage: 95
    }));

    expect(view.textContent).toContain("19 / 20+1 fuera de padrón");
    expect(view.textContent).toContain("95% completado");
  });

  it("filters nominal students by missing status", () => {
    const view = render(progress());
    const missingButton = [...view.querySelectorAll("button")].find(button => button.textContent?.includes("Faltan"));
    act(() => missingButton?.click());

    expect(view.textContent).toContain("Brenda Missing");
    expect(view.textContent).not.toContain("Ana Working");
  });

  it("labels teacher-added students and allows removing them before they start", () => {
    const remove = vi.fn(async () => undefined);
    const view = render(progress({ students: [student({ id: "extra-1", displayName: "Bruno Díaz", status: "not_started", offRoster: true })] }), session, { onRemoveExtraStudent: remove });
    expect(view.textContent).toContain("Fuera de padrón");
    act(() => [...view.querySelectorAll("button")].find(button => button.textContent === "Quitar")?.click());
    expect(remove).toHaveBeenCalledWith("extra-1");
  });

  it("opens the off-roster student form only for nominal sessions", () => {
    const view = render(progress(), session, { onAddExtraStudent: vi.fn(async () => undefined) });
    act(() => [...view.querySelectorAll("button")].find(button => button.textContent === "Agregar alumno fuera de padrón")?.click());
    expect(view.textContent).toContain("DNI");
    expect(view.textContent).toContain("Nombre");
    expect(view.textContent).toContain("Apellido");
    expect(view.querySelectorAll("input")).toHaveLength(4);
  });

  it("shows only started students without a roster and keeps school and grade context", () => {
    const nonNominalSession = { ...session, rosterSectionId: null, rosterSnapshotId: null };
    const view = render(progress({
      gradeLabel: null,
      students: [student({ id: "working", displayName: "Ana Working", status: "in_progress" }), student({ id: "done", displayName: "Cecilia Done", status: "submitted", submittedAt: "2026-10-01T10:14:00Z" })]
    }), nonNominalSession);

    expect(view.textContent).toContain("Sesión sin padrón: se muestran solo quienes ingresaron.");
    expect(view.textContent).not.toContain("Faltan");
    expect(view.textContent).toContain("6° A · Turno mañana");
    expect(view.textContent).toContain("Ana Working");
    expect(view.textContent).toContain("Cecilia Done");
    expect(view.textContent).toContain("Entregó");
  });

  it("uses the first progress result as a baseline and highlights later submissions", () => {
    const view = render(null);
    const alreadySubmitted = student({ id: "done", displayName: "Cecilia Done", status: "submitted", submittedAt: "2026-10-01T10:14:00Z" });
    rerender(progress({ submittedCount: 1, inProgressCount: 0, students: [alreadySubmitted] }));
    expect(view.querySelector(".student-row-submitted")).toBeNull();

    rerender(progress({ inProgressCount: 1, submittedCount: 0, students: [student({ id: "done", displayName: "Cecilia Done", status: "in_progress" })] }));
    rerender(progress({ submittedCount: 1, inProgressCount: 0, students: [alreadySubmitted] }));
    expect(view.querySelector(".student-row-submitted")?.textContent).toContain("Cecilia Done");
  });

  it("exposes pause, resume, close confirmation and discard confirmation", () => {
    const changeStatus = vi.fn(async () => ({ submitted: 1, failed: 0 }));
    const discard = vi.fn(async () => true);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    const view = render(progress(), session, { onStatusChange: changeStatus, onDiscard: discard });
    const buttons = [...view.querySelectorAll("button")];
    act(() => buttons.find(button => button.textContent === "Pausar")?.click());
    act(() => buttons.find(button => button.textContent === "Cerrar sesión")?.click());
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining("Hay 1 estudiantes rindiendo"));
    expect(changeStatus).toHaveBeenNthCalledWith(1, "paused");
    expect(changeStatus).toHaveBeenNthCalledWith(2, "closed");

    const paused = render(progress(), { ...session, status: "paused" }, { onStatusChange: changeStatus });
    act(() => [...paused.querySelectorAll("button")].find(button => button.textContent === "Reanudar")?.click());
    expect(changeStatus).toHaveBeenLastCalledWith("active");

    const empty = render(progress({ startedCount: 0, inProgressCount: 0, students: [] }), session, { onDiscard: discard });
    act(() => [...empty.querySelectorAll("button")].find(button => button.textContent === "Descartar sesión")?.click());
    expect(confirm).toHaveBeenLastCalledWith("Se va a borrar la sesión y no quedará en el historial.");
    expect(discard).toHaveBeenCalledOnce();
  });

  it("shows a read-only close summary with nominal missing and teacher-submitted students", () => {
    const closed = { ...session, status: "closed" };
    const view = render(progress({ submittedCount: 1, inProgressCount: 0, startedCount: 1, students: [
      student({ id: "done", displayName: "Entregada, Ana", status: "submitted", submittedAt: "2026-10-01T10:14:00Z", submissionReason: "closed_by_teacher" }),
      student({ id: "missing", displayName: "Ausente, Brenda", status: "not_started" })
    ] }), closed, { onReturn: vi.fn(), onStats: vi.fn(), onRemoveExtraStudent: vi.fn() });
    expect(view.textContent).toContain("Entregaron 1 de 2.");
    expect(view.textContent).toContain("No rindieron: Brenda Ausente.");
    expect(view.textContent).toContain("Entregados por cierre: Ana Entregada.");
    const summary = view.querySelector(".session-close-summary")!;
    const studentList = view.querySelector(".session-students")!;
    expect(Boolean(summary.compareDocumentPosition(studentList) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true);
    expect(view.textContent).not.toContain("Enlace para estudiantes");
    expect(view.querySelector(".progress-summary")).toBeNull();
    const closedSubmissionRow = [...view.querySelectorAll("tbody tr")].find(row => row.textContent?.includes("Entregado por cierre"));
    expect(closedSubmissionRow?.querySelector('[data-label="Entregó"]')?.textContent).toMatch(/\d{2}:\d{2}/);
    expect(view.querySelectorAll(".session-close-summary .button")).toHaveLength(2);
    expect([...view.querySelectorAll(".student-filter")].map(button => button.textContent?.trim())).toEqual(["Todos 2", "Faltan 1", "Entregaron 1"]);
    expect([...view.querySelectorAll("thead th")].map(header => header.textContent)).toEqual(["Nombre", "DNI", "Estado", "Entregó"]);
    expect(view.textContent).not.toContain("Pausar");
    expect(view.textContent).toContain("Volver al inicio");
    expect(view.textContent).toContain("Ver estadísticas");
  });

  it("keeps the roster label and missing filter after every rostered student has started", () => {
    const closed = { ...session, status: "closed" };
    const view = render(progress({ hasRoster: true, startedCount: 2, submittedCount: 2, inProgressCount: 0, students: [
      student({ id: "one", displayName: "Ana Entregada", status: "submitted" }),
      student({ id: "two", displayName: "Bruno Entregado", status: "submitted" })
    ] }), closed);
    expect(view.textContent).toContain("Faltan");
    expect(view.querySelector(".student-filter")?.textContent).toContain("Todos");
    expect([...view.querySelectorAll(".student-filter")].some(button => button.textContent?.includes("Rindiendo"))).toBe(false);
    expect(view.textContent).not.toContain("Sesión sin padrón");
  });
});
