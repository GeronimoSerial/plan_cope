// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
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

  function render(progressData: SessionProgress | null, sessionData = session) {
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    act(() => root?.render(<ActiveSessionPanel progress={progressData} session={sessionData} sessionLink="http://local/ABC-123" />));
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
});
