import { useEffect, useState } from "react";
import { AppShell } from "./components/AppShell";
import { SessionsWorkspace } from "./components/SessionsWorkspace";
import { StatsWorkspace } from "./components/StatsWorkspace";
import { useDeliverySession } from "./hooks/useDeliverySession";
import { useHostContext } from "./hooks/useHostContext";
import { ActivationScreen, shouldShowActivation } from "./activation/ActivationScreen";
import { isValidCue } from "./domain/cue";

export function HostApp() {
  const hostContext = useHostContext();
  const delivery = useDeliverySession(hostContext);
  const [isLocked, setIsLocked] = useState(false);
  const [activationInProgress, setActivationInProgress] = useState(false);
  const [activationRetryAvailable, setActivationRetryAvailable] = useState(false);
  const [activationStatusChecked, setActivationStatusChecked] = useState(false);
  const [revalidationDaysRemaining, setRevalidationDaysRemaining] = useState<number | null>(null);
  const [expiryPending, setExpiryPending] = useState(false);
  const [localClockWarning, setLocalClockWarning] = useState(false);
  const [activeTab, setActiveTab] = useState<"home" | "history" | "stats">("home");
  const clearManualCue = () => delivery.sessionForm.updateForm("cue", "");
  const changeTab = (tab: "home" | "history" | "stats") => {
    clearManualCue();
    delivery.activeSession.returnToSessions();
    setActiveTab(tab);
  };

  useEffect(() => {
    let cancelled = false;
    const checkLockStatus = () => {
      fetch(`${hostContext.apiBaseUrl}/api/activation/status`)
        .then(response => (response.ok ? response.json() : null))
        .then(data => {
          if (!cancelled && data && typeof data.isLocked === "boolean") {
            setIsLocked(data.isLocked);
            setActivationInProgress(data.activationInProgress === true);
            setActivationRetryAvailable(data.retryAvailable === true);
            setRevalidationDaysRemaining(typeof data.revalidationDaysRemaining === "number" ? data.revalidationDaysRemaining : null);
            setExpiryPending(data.expiryPending === true);
            setLocalClockWarning(data.localClockWarning === true);
          }
          if (!cancelled) setActivationStatusChecked(true);
        })
        .catch(() => {
          /* transient failure — keep the last known lock state, do not flip to unlocked */
          if (!cancelled) setActivationStatusChecked(true);
        });
    };
    checkLockStatus();
    const interval = setInterval(checkLockStatus, 15000);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [hostContext.apiBaseUrl]);

  if (!activationStatusChecked) {
    return <main className="school-gate" aria-busy="true"><p role="status">Verificando la activación…</p></main>;
  }

  if (isLocked) {
    return (
      <ActivationScreen apiBaseUrl={hostContext.apiBaseUrl} isLocked />
    );
  }

  if (shouldShowActivation(hostContext.isActivated, activationInProgress)) {
    return <ActivationScreen apiBaseUrl={hostContext.apiBaseUrl}
      activationInProgress={activationInProgress} retryAvailable={activationRetryAvailable} />;
  }

  return (
    <AppShell status={delivery.status} apiBaseUrl={hostContext.apiBaseUrl} appVersion={hostContext.appVersion} activeSessionGradeLabel={delivery.activeSession.session ? delivery.activeSession.progress?.gradeLabel : null} activeTab={activeTab} onTabChange={changeTab}>
      {localClockWarning && <p className="sync-warning" role="alert">La fecha y hora de este equipo son incorrectas. Corregilas para mantener la revalidación al día.</p>}
      {expiryPending && <p className="sync-warning" role="status">La revalidación está vencida. Finalizá y enviá la evaluación en curso; no inicies otra sesión.</p>}
      {!expiryPending && revalidationDaysRemaining !== null && revalidationDaysRemaining <= 5 && (
        <p className="sync-warning" role="status" aria-live="polite">
          Conectate a internet para revalidar el equipo. Quedan {revalidationDaysRemaining} {revalidationDaysRemaining === 1 ? "día" : "días"}.
        </p>
      )}
      {activeTab !== "stats" ? (
        <SessionsWorkspace delivery={delivery} apiBaseUrl={hostContext.apiBaseUrl} tab={activeTab} expiryPending={expiryPending} onStats={() => { delivery.activeSession.returnToSessions(); setActiveTab("stats"); }} onReturnHome={() => setActiveTab("home")} />
      ) : delivery.activeSession.session ? <SessionsWorkspace delivery={delivery} apiBaseUrl={hostContext.apiBaseUrl} tab="home" expiryPending={expiryPending} onStats={() => { delivery.activeSession.returnToSessions(); setActiveTab("stats"); }} onReturnHome={() => setActiveTab("home")} /> : (
        <StatsWorkspace
          apiBaseUrl={hostContext.apiBaseUrl}
          cue={resolveStatsCue(delivery.sessionForm.form.cue, delivery.activeSession.schools.map(school => school.code))}
          schoolYear={delivery.roster.snapshot?.schoolYear}
        />
      )}
    </AppShell>
  );
}

export function resolveStatsCue(cue: string, schoolCodes: readonly string[]): string {
  return isValidCue(cue) ? cue : schoolCodes[0] ?? "";
}
