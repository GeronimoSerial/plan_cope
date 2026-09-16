import { useState } from "react";
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
  const [activeTab, setActiveTab] = useState<"sessions" | "stats">("sessions");

  if (shouldShowActivation(hostContext.isActivated)) {
    return <ActivationScreen />;
  }

  if (!isSchoolConfirmed) {
    return (
      <SchoolGate
        cue={delivery.sessionForm.form.cue}
        schoolName={delivery.sessionForm.schoolName}
        hasRoster={delivery.roster.snapshot?.status.toLowerCase() === "ready" && delivery.roster.sections.length > 0}
        isLoadingRoster={delivery.roster.isLoading}
        rosterError={delivery.roster.error}
        onCueChange={value => delivery.sessionForm.updateForm("cue", value)}
        onContinue={() => setIsSchoolConfirmed(true)}
      />
    );
  }

  return (
    <AppShell status={delivery.status}>
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
