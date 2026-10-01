// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { HostApp } from "./HostApp";

const mocks = vi.hoisted(() => ({
  useHostContext: vi.fn(),
  useDeliverySession: vi.fn()
}));

vi.mock("./hooks/useHostContext", () => ({ useHostContext: mocks.useHostContext }));
vi.mock("./hooks/useDeliverySession", () => ({ useDeliverySession: mocks.useDeliverySession }));
vi.mock("./components/AppShell", () => ({ AppShell: ({ children }: { children: React.ReactNode }) => <main>{children}</main> }));
vi.mock("./components/SessionsWorkspace", () => ({ SessionsWorkspace: () => <div data-testid="sessions-workspace" /> }));
vi.mock("./components/StatsWorkspace", () => ({ StatsWorkspace: () => <div data-testid="stats-workspace" /> }));
vi.mock("./activation/ActivationScreen", () => ({
  ActivationScreen: ({ isLocked, isRevoked }: { isLocked?: boolean; isRevoked?: boolean }) =>
    <section data-testid="activation-screen" data-locked={String(isLocked === true)} data-revoked={String(isRevoked === true)} />,
  shouldShowActivation: (isActivated: boolean, activationInProgress = false) => !isActivated || activationInProgress
}));

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });

type ActivationStatus = { isLocked: boolean; isRevoked: boolean; activationInProgress: boolean; retryAvailable: boolean };
const status = (values: Partial<ActivationStatus> = {}): ActivationStatus => ({
  isLocked: false, isRevoked: false, activationInProgress: false, retryAvailable: false, ...values
});
const response = (body: ActivationStatus) => Promise.resolve({ ok: true, json: () => Promise.resolve(body) });

let root: Root | undefined;
let container: HTMLDivElement | undefined;
let fetchMock: ReturnType<typeof vi.fn>;
let currentStatus: ActivationStatus;

beforeEach(() => {
  currentStatus = status();
  fetchMock = vi.fn(() => response(currentStatus));
  vi.stubGlobal("fetch", fetchMock);
  mocks.useHostContext.mockReturnValue({
    apiBaseUrl: "http://local", lanBaseUrl: "http://lan", operatorName: "Docente", isActivated: true,
    appVersion: "1.0.0"
  });
  mocks.useDeliverySession.mockReturnValue({
    status: "", error: null, activeSession: { returnToSessions: vi.fn(), session: null, schools: [] },
    sessionForm: { updateForm: vi.fn(), form: { cue: "" } }, roster: { snapshot: null }
  });
});

afterEach(() => {
  if (root) act(() => root?.unmount());
  root = undefined;
  container?.remove();
  container = undefined;
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("HostApp revocation status", () => {
  it("ignores an older status response that arrives after a newer response", async () => {
    vi.useFakeTimers();
    let resolveFirst!: (value: Awaited<ReturnType<typeof response>>) => void;
    fetchMock
      .mockImplementationOnce(() => new Promise(resolve => { resolveFirst = resolve; }))
      .mockImplementation(() => response(status({ isLocked: true, isRevoked: true })));
    const view = render();

    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });
    expect(view.querySelector('[data-testid="activation-screen"]')?.getAttribute("data-locked")).toBe("true");

    await act(async () => { resolveFirst({ ok: true, json: () => Promise.resolve(status()) }); await Promise.resolve(); });
    expect(view.querySelector('[data-testid="activation-screen"]')?.getAttribute("data-locked")).toBe("true");
  });

  it("keeps the revoked banner, opens key entry during activation progress, and resets that view after reactivation", async () => {
    vi.useFakeTimers();
    currentStatus = status({ isRevoked: true, activationInProgress: true });
    const view = render();
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });

    expect(view.textContent).toContain("Cargar nueva clave");
    expect(view.querySelector('[data-testid="activation-screen"]')).toBeNull();
    act(() => view.querySelector("button")!.click());
    expect(view.querySelector('[data-testid="activation-screen"]')?.getAttribute("data-revoked")).toBe("true");

    currentStatus = status();
    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });
    expect(view.querySelector('[data-testid="activation-screen"]')).toBeNull();

    currentStatus = status({ isRevoked: true });
    await act(async () => { await vi.advanceTimersByTimeAsync(15000); });
    expect(view.textContent).toContain("Cargar nueva clave");
    expect(view.querySelector('[data-testid="activation-screen"]')).toBeNull();
  });
});

function render() {
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
  act(() => root?.render(<HostApp />));
  return container;
}
