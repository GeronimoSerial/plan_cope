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
  afterEach(() => { if (root) act(() => root?.unmount()); root = undefined; container?.remove(); container = undefined; vi.useRealTimers(); vi.restoreAllMocks(); });

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

  it("searches ready schools and reaches session creation after selection", () => {
    const state = delivery([]);
    const view = render(state);
    act(() => button(view, "Nueva sesión").click());
    const schoolInput = view.querySelector<HTMLInputElement>('#new-session-school[role="combobox"]')!;
    expect(schoolInput).not.toBeNull();
    act(() => { schoolInput.focus(); setInputValue(schoolInput, "norte"); });
    expect(view.querySelectorAll('[role="option"]')).toHaveLength(1);
    act(() => view.querySelector<HTMLElement>('[role="option"]')!.click());
    expect(view.textContent).toContain("CUE 180055400");
    act(() => button(view, "Continuar").click());
    expect(state.sessionForm.updateForm).toHaveBeenCalledWith("cue", "180055400");
    act(() => button(view, "Volver").click());
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
    expect(view.querySelector(".session-create-workspace .session-create-header")).not.toBeNull();
    expect(view.querySelector(".session-create-panel .exam-selector-field")).not.toBeNull();
    expect(view.querySelector<HTMLSelectElement>("#grade-filter")?.classList.contains("control")).toBe(true);
    expect(view.querySelector<HTMLSelectElement>("#section-filter")?.value).toBe("section");
    act(() => button(view, "Crear sesión").click());
    expect(state.createSession).toHaveBeenCalledOnce();
  });

  it("refreshes the local catalog on focus and every minute while the session form is open", async () => {
    vi.useFakeTimers();
    const state = delivery([]);
    const reloadExamsSilently = vi.fn();
    state.examCatalog.reloadExamsSilently = reloadExamsSilently;
    const view = render(state);

    act(() => button(view, "Nueva sesión").click());
    act(() => button(view, "Ingresar otro CUE").click());
    Object.assign(state.sessionForm.form, { cue: "180055402" });
    Object.assign(state.roster, {
      snapshot: { id: "snapshot", cue: "180055402", schoolYear: "2026", fetchedAt: "2026-10-01", checksum: "hash", sectionCount: 1, studentCount: 1, status: "ready" },
      sections: [{ id: "section", snapshotId: "snapshot", course: "6", division: "A", studentCount: 1 }],
      selectedSectionId: "section"
    });
    act(() => root?.render(<SessionsWorkspace delivery={state} apiBaseUrl="http://local" tab="home" expiryPending={false} onStats={() => undefined} onReturnHome={() => undefined} />));
    act(() => button(view, "Continuar").click());

    expect(reloadExamsSilently).toHaveBeenCalledTimes(1);
    act(() => window.dispatchEvent(new Event("focus")));
    expect(reloadExamsSilently).toHaveBeenCalledTimes(2);
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(reloadExamsSilently).toHaveBeenCalledTimes(3);

    act(() => button(view, "Volver").click());
    act(() => window.dispatchEvent(new Event("focus")));
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000); });
    expect(reloadExamsSilently).toHaveBeenCalledTimes(3);
  });

  it("loads node history and applies school and status filters", async () => {
    const historySession = { ...session("closed", "180055400", "Escuela Norte"), status: "closed", endAt: "2026-10-01T10:35:00Z", offRosterSubmittedCount: 1 };
    const historyLoader = vi.spyOn(ApiClient.prototype, "getSessionHistory").mockResolvedValue({ items: [historySession], page: 1, pageSize: 20, totalCount: 1 });
    vi.spyOn(ApiClient.prototype, "getSessionHistoryFilters").mockResolvedValue([
      { course: "6°", division: "A", shift: "Mañana" }, { course: "6°", division: "A", shift: "Tarde" }
    ]);
    const view = render(delivery([]), "history");
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect(view.textContent).toContain("CODEclosed");
    expect(view.querySelector(".badge-neutral")?.textContent).toBe("Cerrada");
    expect(view.querySelector("td small")?.textContent).toMatch(/\d+ h \d+ min · hasta \d{2}:\d{2}/);
    expect(view.querySelectorAll("tbody tr td")[5].textContent).toBe("4/20+1 fuera de padrón");
    const schoolPicker = view.querySelector<HTMLInputElement>('#history-school[role="combobox"]')!;
    act(() => schoolPicker.focus());
    act(() => view.querySelectorAll<HTMLElement>('[role="option"]')[1].click());
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect(historyLoader).toHaveBeenLastCalledWith({ schoolCode: "180055400", status: "", course: "", division: undefined, shift: undefined, q: "", page: 1, pageSize: 20 }, expect.any(AbortSignal));
    const gradeSelect = view.querySelector<HTMLSelectElement>('[aria-label="Grado"]')!;
    act(() => { gradeSelect.value = "6°"; gradeSelect.dispatchEvent(new Event("change", { bubbles: true })); });
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect([...view.querySelector<HTMLSelectElement>("#history-section")!.options].map(option => option.text)).toEqual(["Todas las secciones", "A · Mañana", "A · Tarde"]);
    const sectionSelect = view.querySelector<HTMLSelectElement>("#history-section")!;
    act(() => { sectionSelect.value = "A\u001fMañana"; sectionSelect.dispatchEvent(new Event("change", { bubbles: true })); });
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect(historyLoader).toHaveBeenLastCalledWith({ schoolCode: "180055400", status: "", course: "6°", division: "A", shift: "Mañana", q: "", page: 1, pageSize: 20 }, expect.any(AbortSignal));
    act(() => { sectionSelect.value = "A\u001fTarde"; sectionSelect.dispatchEvent(new Event("change", { bubbles: true })); });
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect(historyLoader).toHaveBeenLastCalledWith({ schoolCode: "180055400", status: "", course: "6°", division: "A", shift: "Tarde", q: "", page: 1, pageSize: 20 }, expect.any(AbortSignal));
  });

  it("debounces history search and resets pagination to the first page", async () => {
    vi.useFakeTimers();
    const historySession = session("history-result", "180055400", "Escuela Norte");
    let finishSearch: ((page: { items: LocalSession[]; page: number; pageSize: number; totalCount: number }) => void) | undefined;
    const historyLoader = vi.spyOn(ApiClient.prototype, "getSessionHistory").mockImplementation(filters => filters.q
      ? new Promise(resolve => { finishSearch = resolve; })
      : Promise.resolve({ items: [historySession], page: filters.page ?? 1, pageSize: 20, totalCount: 40 }));
    const view = render(delivery([]), "history");
    await act(async () => { for (let index = 0; index < 8; index++) await Promise.resolve(); });
    expect(button(view, "Siguiente").disabled).toBe(false);
    const initialRequestSignal = historyLoader.mock.calls[0][1];
    act(() => button(view, "Siguiente").click());
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    expect(initialRequestSignal?.aborted).toBe(true);
    expect(historyLoader).toHaveBeenLastCalledWith({ schoolCode: "", status: "", course: "", division: undefined, shift: undefined, q: "", page: 2, pageSize: 20 }, expect.any(AbortSignal));
    const search = view.querySelector<HTMLInputElement>("#history-search")!;
    act(() => setInputValue(search, "Álamo"));
    await act(async () => { await vi.advanceTimersByTimeAsync(300); });
    expect(historyLoader).toHaveBeenLastCalledWith({ schoolCode: "", status: "", course: "", division: undefined, shift: undefined, q: "Álamo", page: 1, pageSize: 20 }, expect.any(AbortSignal));
    await act(async () => { await Promise.resolve(); });
    expect(view.textContent).toContain("Buscando…");
    expect(view.querySelectorAll("tbody tr")).toHaveLength(1);
    await act(async () => finishSearch?.({ items: [], page: 1, pageSize: 20, totalCount: 0 }));
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

function setInputValue(input: HTMLInputElement, value: string) {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(input, value);
  input.dispatchEvent(new Event("input", { bubbles: true }));
}

function delivery(sessions: LocalSession[]): DeliverySessionState {
  const state = {
    examCatalog: { exams: [], isLoadingExams: false, selectedExamId: "", setSelectedExamId: vi.fn(), loadExams: vi.fn(), reloadExamsSilently: vi.fn() },
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
