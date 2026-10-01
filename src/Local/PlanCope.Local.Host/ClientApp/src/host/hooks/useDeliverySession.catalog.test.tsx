// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiClient } from "../api/apiClient";
import type { HostContext } from "../types";
import { SessionCreatePanel } from "../components/SessionCreatePanel";
import { useDeliverySession, type DeliverySessionState } from "./useDeliverySession";

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });

const hostContext: HostContext = {
  apiBaseUrl: "http://localhost",
  lanBaseUrl: "http://localhost",
  operatorName: "Docente",
  port: 8080,
  isActivated: true
};

const importedExam = {
  id: "local-exam-1",
  remoteExamVersionId: "remote-version-1",
  examCode: "MAT-6",
  versionNumber: 1,
  checksum: "checksum-1",
  metadataJson: JSON.stringify({ title: "Matemática 6" }),
  schemaVersion: 1,
  syncedAt: "2026-10-01T10:00:00Z"
};

describe("useDeliverySession exam catalog refresh", () => {
  let root: Root | undefined;
  let container: HTMLDivElement | undefined;

  afterEach(() => {
    if (root) act(() => root?.unmount());
    root = undefined;
    container?.remove();
    container = undefined;
    vi.restoreAllMocks();
  });

  it("reloads the local catalog when a successful pull reports no new exams", async () => {
    const getExams = vi.spyOn(ApiClient.prototype, "getExams")
      .mockResolvedValueOnce([])
      .mockResolvedValue([importedExam]);
    vi.spyOn(ApiClient.prototype, "pullExams").mockResolvedValue({
      status: "up_to_date",
      newExams: 0,
      updatedExams: 0,
      totalReceived: 0,
      errorCode: null,
      message: "No hay exámenes nuevos.",
      lastPullAt: "2026-10-01T10:00:00Z"
    });
    vi.spyOn(ApiClient.prototype, "getActiveSessions").mockResolvedValue([]);
    vi.spyOn(ApiClient.prototype, "getSchools").mockResolvedValue([]);

    let delivery: DeliverySessionState | undefined;
    function Harness() {
      delivery = useDeliverySession(hostContext);
      return <div>
        <span>{delivery.examCatalog.exams.map(exam => exam.displayName).join(",")}</span>
        <button onClick={() => void delivery?.syncPull.pullExamsNow()}>Buscar exámenes nuevos</button>
      </div>;
    }

    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    await act(async () => {
      root?.render(<Harness />);
      await flushMicrotasks();
    });
    expect(container.textContent).not.toContain("Matemática 6");
    expect(getExams).toHaveBeenCalledTimes(1);

    await act(async () => {
      container?.querySelector("button")?.click();
      await flushMicrotasks();
    });

    expect(delivery?.syncPull.message).toBe("No hay exámenes nuevos.");
    expect(getExams).toHaveBeenCalledTimes(2);
    expect(container.textContent).toContain("Matemática 6 · versión 1");
  });

  it("keeps the create button enabled and preserves existing errors during a failed silent reload", async () => {
    const getExams = vi.spyOn(ApiClient.prototype, "getExams").mockResolvedValue([importedExam]);
    vi.spyOn(ApiClient.prototype, "pullExams").mockResolvedValue({
      status: "up_to_date",
      newExams: 0,
      updatedExams: 0,
      totalReceived: 0,
      errorCode: null,
      message: "No hay exámenes nuevos.",
      lastPullAt: null
    });
    vi.spyOn(ApiClient.prototype, "getActiveSessions").mockResolvedValue([]);
    vi.spyOn(ApiClient.prototype, "getSchools").mockResolvedValue([]);

    let delivery: DeliverySessionState | undefined;
    function Harness() {
      const current = useDeliverySession(hostContext);
      delivery = current;
      return <>
        <p role="alert">{current.error}</p>
        <SessionCreatePanel
          exams={current.examCatalog.exams}
          formErrors={current.sessionForm.formErrors}
          selectedExamId={current.examCatalog.selectedExamId}
          isBusy={current.isBusy}
          isLoadingExams={current.examCatalog.isLoadingExams}
          onCreateSession={current.createSession}
          onRefreshExams={() => void current.examCatalog.loadExams()}
          onSelectedExamChange={current.examCatalog.setSelectedExamId}
          syncPull={current.syncPull}
          roster={{
            snapshot: { id: "snapshot", cue: "180055402", schoolYear: "2026", fetchedAt: "2026-10-01", checksum: "hash", sectionCount: 1, studentCount: 1, status: "ready" },
            sections: [{ id: "section", snapshotId: "snapshot", course: "6", division: "A", studentCount: 1 }],
            selectedSectionId: "section",
            setSelectedSectionId: () => undefined,
            isLoading: false,
            error: null
          }}
        />
        <button onClick={() => void current.examCatalog.reloadExamsSilently()}>Refresh silencioso</button>
        <button onClick={() => void current.createSession()}>Crear inválida</button>
      </>;
    }

    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
    await act(async () => {
      root?.render(<Harness />);
      await flushMicrotasks();
    });

    await act(async () => {
      button("Crear inválida").click();
      await flushMicrotasks();
    });
    expect(container.querySelector('[role="alert"]')?.textContent).toBe("Revisa los datos marcados para crear la sesion.");

    let rejectReload!: (reason: Error) => void;
    getExams.mockImplementationOnce(() => new Promise((_, reject) => { rejectReload = reject; }));
    await act(async () => {
      button("Refresh silencioso").click();
      await Promise.resolve();
    });
    expect(button("Crear sesión").disabled).toBe(false);
    expect(container.querySelector('[role="alert"]')?.textContent).toBe("Revisa los datos marcados para crear la sesion.");

    await act(async () => {
      rejectReload(new Error("Catalog refresh failed"));
      await flushMicrotasks();
    });
    expect(button("Crear sesión").disabled).toBe(false);
    expect(container.querySelector('[role="alert"]')?.textContent).toBe("Revisa los datos marcados para crear la sesion.");
    expect(container.textContent).toContain("Matemática 6 · versión 1");
  });
});

async function flushMicrotasks() {
  for (let index = 0; index < 8; index += 1) await Promise.resolve();
}

function button(label: string): HTMLButtonElement {
  const found = [...document.querySelectorAll("button")].find(candidate => candidate.textContent === label);
  if (!found) throw new Error(`Button not found: ${label}`);
  return found;
}
