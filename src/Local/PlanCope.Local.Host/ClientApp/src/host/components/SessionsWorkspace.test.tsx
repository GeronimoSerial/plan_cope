// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiClient } from "../api/apiClient";
import type { DeliverySessionState } from "../hooks/useDeliverySession";
import type { LocalSession } from "../types";
import { SessionsWorkspace } from "./SessionsWorkspace";

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });

const session = (id: string, schoolCode: string, schoolName: string): LocalSession => ({
  id, schoolCode, schoolName, examVersionId: "exam-1", examTitle: "Matemática 6", gradeLabel: "6° A · Turno mañana",
  startedBy: "Docente", startAt: "2026-10-01T10:00:00Z", status: "active", accessCode: `CODE${id}`, expectedStudentCount: 20,
  submittedCount: 4, inProgressCount: 2, rosterSnapshotId: "snapshot", rosterSectionId: "section"
});

describe("SessionsWorkspace", () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;
  afterEach(() => { if (root) act(() => root?.unmount()); root = undefined; container?.remove(); container = undefined; vi.restoreAllMocks(); });

  it("shows multiple node sessions before a school is selected and waits for an explicit selection", () => {
    const state = delivery([session("a", "180055400", "Escuela Norte"), { ...session("b", "180055401", "Escuela Sur"), offRosterSubmittedCount: 1 }]);
    const view = render(state);
    expect(view.textContent).toContain("Escuela Norte");
    expect(view.textContent).toContain("Escuela Sur");
    expect(view.textContent).not.toContain("Código de sesión");
    expect(view.querySelector(".badge-info")?.textContent).toBe("Abierta");
    expect(view.querySelector(".node-session-card-heading strong")?.textContent).toBe("Escuela Norte");
    expect(view.querySelector(".node-session-card-heading .badge")?.textContent).toBe("Abierta");
    expect(view.querySelector(".node-session-card-copy > span")?.textContent).toBe("6° A · Turno mañana · Matemática 6");
    const firstCard = view.querySelector(".node-session-card")!;
    expect([...firstCard.querySelectorAll(".node-session-meta span")].map(item => item.textContent)).toEqual([expect.stringMatching(/^Inicio /), "Entregaron 4/20"]);
    const secondCard = view.querySelectorAll(".node-session-card")[1];
    expect([...secondCard.querySelectorAll(".node-session-meta span")].map(item => item.textContent)).toContain("Entregaron 4/20 +1 fuera de padrón");
    expect(view.querySelector(".node-session-card .button")?.textContent).toBe("Ver");
    expect(state.activeSession.selectSession).not.toHaveBeenCalled();
    button(view, "Ver").click();
    expect(state.activeSession.selectSession).toHaveBeenCalledWith(state.activeSession.activeSessions[0]);
  });

  it("clears a manually entered CUE when the teacher cancels that step", () => {
    const state = delivery([]);
    const view = render(state);
    act(() => button(view, "Nueva sesión").click());
    act(() => button(view, "Ingresar otro CUE").click());
    act(() => button(view, "Cancelar").click());
    expect(state.sessionForm.updateForm).toHaveBeenCalledWith("cue", "");
  });

  it("asks for a CUE and reaches session creation for a ready roster", () => {
    const state = delivery([]);
    const view = render(state);
    act(() => button(view, "Nueva sesión").click());
    expect(view.textContent).toContain("Escuela Norte");
    expect(view.textContent).toContain("Elegir");
    expect(view.querySelector(".school-choice")).not.toBeNull();
    act(() => button(view, "Ingresar otro CUE").click());
    expect(view.querySelector('input[maxlength="9"]')).not.toBeNull();
    expect(view.textContent).toContain("Ingresar otro CUE");
    Object.assign(state.sessionForm.form, { cue: "180055402" });
    Object.assign(state.roster, {
      snapshot: { id: "snapshot", cue: "180055402", schoolYear: "2026", fetchedAt: "2026-10-01", checksum: "hash", sectionCount: 1, studentCount: 1, status: "ready" },
      sections: [{ id: "section", snapshotId: "snapshot", course: "6", division: "A", studentCount: 1 }],
      selectedSectionId: "section"
    });
    Object.assign(state.examCatalog, { exams: [{ id: "exam-1", displayName: "Matemática 6" }], selectedExamId: "exam-1" });
    act(() => root?.render(<SessionsWorkspace delivery={state} apiBaseUrl="http://local" tab="home" expiryPending={false} onStats={() => undefined} onReturnHome={() => undefined} />));
    act(() => button(view, "Continuar").click());
    expect(view.textContent).toContain("Crear sesión");
    act(() => button(view, "Crear sesión").click());
    expect(state.createSession).toHaveBeenCalledOnce();
  });

  it("loads node history and applies school and status filters", async () => {
    const historySession = { ...session("closed", "180055400", "Escuela Norte"), status: "closed", endAt: "2026-10-01T10:35:00Z", offRosterSubmittedCount: 1 };
    const historyLoader = vi.spyOn(ApiClient.prototype, "getSessionHistory").mockResolvedValue({ items: [historySession], page: 1, pageSize: 20, totalCount: 1 });
    const view = render(delivery([]), "history");
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect(view.textContent).toContain("CODEclosed");
    expect(view.querySelector(".badge-neutral")?.textContent).toBe("Cerrada");
    expect(view.querySelector("td small")?.textContent).toMatch(/\d+ h \d+ min · hasta \d{2}:\d{2}/);
    expect(view.querySelectorAll("tbody tr td")[5].textContent).toBe("4/20+1 fuera de padrón");
    const selects = view.querySelectorAll("select");
    act(() => { Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set?.call(selects[0], "180055400"); selects[0].dispatchEvent(new Event("change", { bubbles: true })); });
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect(historyLoader).toHaveBeenLastCalledWith({ schoolCode: "180055400", status: "", page: 1, pageSize: 20 }, expect.any(AbortSignal));
  });

  function render(state: DeliverySessionState, tab: "home" | "history" = "home") {
    container = document.createElement("div"); document.body.append(container); root = createRoot(container);
    act(() => root?.render(<SessionsWorkspace delivery={state} apiBaseUrl="http://local" tab={tab} expiryPending={false} onStats={() => undefined} onReturnHome={() => undefined} />));
    return container;
  }
});

function button(view: HTMLElement, label: string): HTMLButtonElement {
  const found = [...view.querySelectorAll("button")].find(item => item.textContent?.trim() === label);
  if (!found) throw new Error(`Button not found: ${label}`);
  return found;
}

function delivery(sessions: LocalSession[]): DeliverySessionState {
  const state = {
    examCatalog: { exams: [], isLoadingExams: false, selectedExamId: "", setSelectedExamId: vi.fn(), loadExams: vi.fn() },
    syncPull: { isPulling: false, message: null, lastPullAt: null, pullExamsNow: vi.fn() },
    roster: { snapshot: null, sections: [], selectedSectionId: "", setSelectedSectionId: vi.fn(), isLoading: false, error: null },
    sessionForm: { form: { cue: "", classroomCode: "", commissionCode: "", operatorName: "Docente", expectedStudentCount: 0 }, formErrors: {}, schoolName: "", updateForm: vi.fn() },
    activeSession: { session: null, progress: null, sessionLink: "", activeSessions: sessions, schools: [
      { code: "180055400", name: "Escuela Norte", hasReadyRoster: true }, { code: "180055401", name: "Escuela Sur", hasReadyRoster: true }
    ], resumeAccessCode: "", setResumeAccessCode: vi.fn(), resumeSession: vi.fn(), selectSession: vi.fn(), updateSessionStatus: vi.fn(), discardSession: vi.fn(), returnToSessions: vi.fn() },
    status: "", error: null, isBusy: false, createSession: vi.fn()
  };
  return state as unknown as DeliverySessionState;
}
