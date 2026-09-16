import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createProgressPoller } from "./useDeliverySession";
import type { SessionProgress } from "../types";

const ACCESS_CODE = "ABC-123";

function progress(overrides: Partial<SessionProgress> = {}): SessionProgress {
  return {
    sessionId: "session-1",
    accessCode: ACCESS_CODE,
    expectedStudentCount: 20,
    startedCount: 10,
    submittedCount: 2,
    inProgressCount: 5,
    completionPercentage: 10,
    ...overrides
  };
}

const CHANGED_PROGRESS = progress({ submittedCount: 3, inProgressCount: 4, completionPercentage: 15 });

const visibilityListeners = new Set<EventListener>();

let fakeDocument: {
  visibilityState: string;
  addEventListener: (type: string, handler: EventListener) => void;
  removeEventListener: (type: string, handler: EventListener) => void;
};

function setVisibility(state: "visible" | "hidden"): void {
  fakeDocument.visibilityState = state;
  for (const listener of visibilityListeners) {
    listener({ type: "visibilitychange" } as Event);
  }
}

beforeEach(() => {
  visibilityListeners.clear();
  vi.useFakeTimers();
  fakeDocument = {
    visibilityState: "visible",
    addEventListener: (type, handler) => {
      if (type === "visibilitychange") {
        visibilityListeners.add(handler);
      }
    },
    removeEventListener: (type, handler) => {
      if (type === "visibilitychange") {
        visibilityListeners.delete(handler);
      }
    }
  };
  vi.stubGlobal("document", fakeDocument);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("createProgressPoller", () => {
  it("does not poll when there is no active session", async () => {
    const fetchProgress = vi.fn();
    const onProgress = vi.fn();
    const onError = vi.fn();

    const dispose = createProgressPoller({
      accessCode: "",
      fetchProgress,
      onProgress,
      onError
    });

    await vi.advanceTimersByTimeAsync(30_000);

    expect(fetchProgress).not.toHaveBeenCalled();
    expect(onProgress).not.toHaveBeenCalled();
    expect(onError).not.toHaveBeenCalled();

    dispose();
  });

  it("backs off when consecutive polls report unchanged progress", async () => {
    const fetchProgress = vi.fn().mockResolvedValue(progress());
    const onProgress = vi.fn();
    const onError = vi.fn();

    const dispose = createProgressPoller({
      accessCode: ACCESS_CODE,
      fetchProgress,
      onProgress,
      onError
    });

    await vi.advanceTimersByTimeAsync(0);
    expect(fetchProgress).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(30_000);
    // Unchanged polls double the interval (3s, 6s, 12s, 24s), so calls land at
    // t=0, 3s, 9s and 21s. A naive fixed 3s cadence would fire 11 times here.
    expect(fetchProgress).toHaveBeenCalledTimes(4);
    expect(onProgress).toHaveBeenCalledTimes(4);

    dispose();
  });

  it("resets to the fast cadence when a poll observes a progress change", async () => {
    let call = 0;
    const fetchProgress = vi.fn(async () => {
      call += 1;
      return call <= 2 ? progress() : CHANGED_PROGRESS;
    });
    const onProgress = vi.fn();
    const onError = vi.fn();

    const dispose = createProgressPoller({
      accessCode: ACCESS_CODE,
      fetchProgress,
      onProgress,
      onError
    });

    await vi.advanceTimersByTimeAsync(0);
    expect(fetchProgress).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(3000); // unchanged -> next poll at t=9s
    await vi.advanceTimersByTimeAsync(6000); // t=9s: changed -> cadence resets to 3s
    expect(fetchProgress).toHaveBeenCalledTimes(3);
    expect(onProgress).toHaveBeenLastCalledWith(CHANGED_PROGRESS);

    // Without the reset the next poll would only fire at t=15s; with the reset
    // it fires at t=12s, the 3s cadence restored immediately.
    await vi.advanceTimersByTimeAsync(3000);
    expect(fetchProgress).toHaveBeenCalledTimes(4);

    dispose();
  });

  it("pauses while the document is hidden and resumes at the fast cadence when it becomes visible", async () => {
    let call = 0;
    const fetchProgress = vi.fn(async () => {
      call += 1;
      return call === 1 ? progress() : CHANGED_PROGRESS;
    });
    const onProgress = vi.fn();
    const onError = vi.fn();

    const dispose = createProgressPoller({
      accessCode: ACCESS_CODE,
      fetchProgress,
      onProgress,
      onError
    });

    await vi.advanceTimersByTimeAsync(0);
    expect(fetchProgress).toHaveBeenCalledTimes(1);

    setVisibility("hidden");
    const callsWhileHidden = fetchProgress.mock.calls.length;
    await vi.advanceTimersByTimeAsync(30_000);
    expect(fetchProgress.mock.calls.length).toBe(callsWhileHidden);

    setVisibility("visible");
    await vi.advanceTimersByTimeAsync(3000);
    expect(fetchProgress).toHaveBeenCalledTimes(callsWhileHidden + 1);

    await vi.advanceTimersByTimeAsync(3000);
    expect(fetchProgress).toHaveBeenCalledTimes(callsWhileHidden + 2);

    dispose();
    expect(visibilityListeners.size).toBe(0);
  });
});