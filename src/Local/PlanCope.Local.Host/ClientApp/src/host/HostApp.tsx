import { useEffect, useState } from "react";
import { AppShell } from "./components/AppShell";
import { SchoolGate } from "./components/SchoolGate";
import { SessionsWorkspace } from "./components/SessionsWorkspace";
import { StatsWorkspace } from "./components/StatsWorkspace";
import { useDeliverySession } from "./hooks/useDeliverySession";
import { useHostContext } from "./hooks/useHostContext";
import { ActivationScreen, shouldShowActivation } from "./activation/ActivationScreen";

export function HostApp() {
  const hostContext = useHostContext();
  const delivery = useDeliverySession(hostContext);
  const [isSchoolConfirmed, setIsSchoolConfirmed] = useState(false);
  const [isLocked, setIsLocked] = useState(false);
  const [activationInProgress, setActivationInProgress] = useState(false);
  const [activationStatusChecked, setActivationStatusChecked] = useState(false);
  const [revalidationDaysRemaining, setRevalidationDaysRemaining] = useState<number | null>(null);
  const [expiryPending, setExpiryPending] = useState(false);
  const [localClockWarning, setLocalClockWarning] = useState(false);
  const [activeTab, setActiveTab] = useState<"sessions" | "stats">("sessions");

  useEffect(() => {
    let cancelled = false;
    const checkLockStatus = () => {
      fetch(`${hostContext.apiBaseUrl}/api/activation/status`)
        .then(response => (response.ok ? response.json() : null))
        .then(data => {
          if (!cancelled && data && typeof data.isLocked === "boolean") {
            setIsLocked(data.isLocked);
            setActivationInProgress(data.activationInProgress === true);
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
      <ActivationScreen apiBaseUrl={hostContext.apiBaseUrl} />
    );
  }

  if (shouldShowActivation(hostContext.isActivated, activationInProgress)) {
    return <ActivationScreen apiBaseUrl={hostContext.apiBaseUrl} activationInProgress={activationInProgress} />;
  }

  if (!isSchoolConfirmed) {
    return (
      <>
        {localClockWarning && <p className="sync-warning" role="alert">La fecha y hora de este equipo son incorrectas. Corregilas para mantener la revalidación al día.</p>}
        {expiryPending && <p className="sync-warning" role="status">La revalidación está vencida. Finalizá y enviá la evaluación en curso; no inicies otra sesión.</p>}
        {!expiryPending && revalidationDaysRemaining !== null && revalidationDaysRemaining <= 5 && (
          <p className="sync-warning" role="status" aria-live="polite">
            Conectate a internet para revalidar el equipo. Quedan {revalidationDaysRemaining} {revalidationDaysRemaining === 1 ? "día" : "días"}.
          </p>
        )}
        <SchoolGate
          cue={delivery.sessionForm.form.cue}
          schoolName={delivery.sessionForm.schoolName}
          hasRoster={delivery.roster.snapshot?.status.toLowerCase() === "ready" && delivery.roster.sections.length > 0}
          isLoadingRoster={delivery.roster.isLoading}
          rosterError={delivery.roster.error}
          onCueChange={value => delivery.sessionForm.updateForm("cue", value)}
          onContinue={() => setIsSchoolConfirmed(true)}
        />
      </>
    );
  }

  return (
    <AppShell status={delivery.status} apiBaseUrl={hostContext.apiBaseUrl} appVersion={hostContext.appVersion}>
      {localClockWarning && <p className="sync-warning" role="alert">La fecha y hora de este equipo son incorrectas. Corregilas para mantener la revalidación al día.</p>}
      {expiryPending && <p className="sync-warning" role="status">La revalidación está vencida. Finalizá y enviá la evaluación en curso; no inicies otra sesión.</p>}
      {!expiryPending && revalidationDaysRemaining !== null && revalidationDaysRemaining <= 5 && (
        <p className="sync-warning" role="status" aria-live="polite">
          Conectate a internet para revalidar el equipo. Quedan {revalidationDaysRemaining} {revalidationDaysRemaining === 1 ? "día" : "días"}.
        </p>
      )}
      <div className="mode-tabs">
        <button
          type="button"
          className={activeTab === "sessions" ? "mode-tab mode-tab-active" : "mode-tab"}
          onClick={() => setActiveTab("sessions")}
        >
          Sesiones
        </button>
        <button
          type="button"
          className={activeTab === "stats" ? "mode-tab mode-tab-active" : "mode-tab"}
          onClick={() => setActiveTab("stats")}
        >
          Estadísticas
        </button>
      </div>
      {activeTab === "sessions" ? (
        <SessionsWorkspace delivery={delivery} />
      ) : (
        <StatsWorkspace
          apiBaseUrl={hostContext.apiBaseUrl}
          cue={delivery.sessionForm.form.cue}
          schoolYear={delivery.roster.snapshot?.schoolYear}
        />
      )}
    </AppShell>
  );
}
