// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiClient } from "../api/apiClient";
import type { HostContext } from "../types";
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
});

async function flushMicrotasks() {
  for (let index = 0; index < 8; index += 1) await Promise.resolve();
}
