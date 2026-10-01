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
    students: [],
    gradeLabel: null,
    course: null,
    division: null,
    shift: null,
    level: null,
    ...overrides
  };
}

const CHANGED_PROGRESS = progress({ submittedCount: 3, inProgressCount: 4, completionPercentage: 15 });

beforeEach(() => {
  vi.useFakeTimers();
  vi.stubGlobal("document", { visibilityState: "visible" });
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

  it("uses a 3 second cadence and backs off to no more than 5 seconds when unchanged", async () => {
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

    await vi.advanceTimersByTimeAsync(3000);
    expect(fetchProgress).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(5000);
    expect(fetchProgress).toHaveBeenCalledTimes(3);
    await vi.advanceTimersByTimeAsync(10_000);
    expect(fetchProgress).toHaveBeenCalledTimes(5);

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

    await vi.advanceTimersByTimeAsync(3000); // unchanged -> next poll at t=8s
    await vi.advanceTimersByTimeAsync(5000); // t=8s: changed -> cadence resets to 3s
    expect(fetchProgress).toHaveBeenCalledTimes(3);
    expect(onProgress).toHaveBeenLastCalledWith(CHANGED_PROGRESS);

    // The changed result restores the 3 second cadence.
    await vi.advanceTimersByTimeAsync(3000);
    expect(fetchProgress).toHaveBeenCalledTimes(4);

    dispose();
  });

  it("keeps polling while the document is hidden", async () => {
    vi.stubGlobal("document", { visibilityState: "hidden" });
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

    await vi.advanceTimersByTimeAsync(3000);
    expect(fetchProgress).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(3000);
    expect(fetchProgress).toHaveBeenCalledTimes(3);

    dispose();
  });

  it("treats a student status change as changed progress even when counters stay the same", async () => {
    const submittedStudent = { id: "student-1", displayName: "Ana", maskedDocument: "**123", status: "submitted" as const, startedAt: null, submittedAt: "2026-10-01T10:00:00Z", attemptId: "attempt-1", submissionReason: null, offRoster: false };
    let call = 0;
    const fetchProgress = vi.fn(async () => {
      call += 1;
      return call === 1 ? progress({ students: [{ ...submittedStudent, status: "in_progress", submittedAt: null }] }) : progress({ students: [submittedStudent] });
    });
    const dispose = createProgressPoller({ accessCode: ACCESS_CODE, fetchProgress, onProgress: vi.fn(), onError: vi.fn() });

    await vi.advanceTimersByTimeAsync(0);
    await vi.advanceTimersByTimeAsync(3000);
    await vi.advanceTimersByTimeAsync(3000);
    expect(fetchProgress).toHaveBeenCalledTimes(3);
    dispose();
  });
});
