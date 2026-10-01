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
    inProgressCount: 1, completionPercentage: 0,
    students: [student({ id: "missing", displayName: "Brenda Missing", status: "not_started" }), student({ id: "working", displayName: "Ana Working", status: "in_progress", maskedDocument: "•••456" })],
    gradeLabel: "6° A · Turno mañana", course: "6°", division: "A", shift: "Mañana", level: "Primario", ...overrides
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

  function render(progressData: SessionProgress | null, sessionData = session, callbacks: { onStatusChange?: (status: "active" | "paused" | "closed") => Promise<{ submitted: number; failed: number } | void | null>; onDiscard?: () => Promise<boolean> } = {}) {
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
    expect(view.textContent).toContain("Entregaron0 / 2");
    expect(view.textContent).toContain("Rindiendo1");
    expect(view.textContent).toContain("Faltan1");
    expect(view.querySelector("table")?.querySelectorAll("th")).toHaveLength(4);
    expect(view.textContent).toContain("•••456");
  });

  it("filters nominal students by missing status", () => {
    const view = render(progress());
    const missingButton = [...view.querySelectorAll("button")].find(button => button.textContent?.includes("Faltan"));
    act(() => missingButton?.click());

    expect(view.textContent).toContain("Brenda Missing");
    expect(view.textContent).not.toContain("Ana Working");
  });

  it("shows only started students without a roster and hides the missing filter and grade when absent", () => {
    const nonNominalSession = { ...session, rosterSectionId: null, rosterSnapshotId: null };
    const view = render(progress({
      gradeLabel: null,
      students: [student({ id: "working", displayName: "Ana Working", status: "in_progress" }), student({ id: "done", displayName: "Cecilia Done", status: "submitted", submittedAt: "2026-10-01T10:14:00Z" })]
    }), nonNominalSession);

    expect(view.textContent).toContain("Sesión sin padrón: se muestran solo quienes ingresaron.");
    expect(view.textContent).not.toContain("Faltan");
    expect(view.textContent).not.toContain("6° A · Turno mañana");
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
      student({ id: "done", displayName: "Ana Entregada", status: "submitted", submissionReason: "closed_by_teacher" }),
      student({ id: "missing", displayName: "Brenda Ausente", status: "not_started" })
    ] }), closed);
    expect(view.textContent).toContain("Entregaron 1 de 2.");
    expect(view.textContent).toContain("No rindieron: Brenda Ausente.");
    expect(view.textContent).toContain("Entregados por cierre: Ana Entregada.");
    expect(view.textContent).not.toContain("Pausar");
    expect(view.textContent).toContain("Volver al inicio");
  });

  it("keeps the roster label and missing filter after every rostered student has started", () => {
    const closed = { ...session, status: "closed" };
    const view = render(progress({ hasRoster: true, startedCount: 2, submittedCount: 2, inProgressCount: 0, students: [
      student({ id: "one", displayName: "Ana Entregada", status: "submitted" }),
      student({ id: "two", displayName: "Bruno Entregado", status: "submitted" })
    ] }), closed);
    expect(view.textContent).toContain("Faltan");
    expect(view.textContent).not.toContain("Sesión sin padrón");
  });
});
