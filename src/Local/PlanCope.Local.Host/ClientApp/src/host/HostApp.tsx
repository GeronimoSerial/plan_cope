import { useState } from "react";
import { AppShell } from "./components/AppShell";
import { SchoolGate } from "./components/SchoolGate";
import { SessionsWorkspace } from "./components/SessionsWorkspace";
import { WorkspaceModeTabs } from "./components/WorkspaceModeTabs";
import { ExamBuilderPage } from "./exam-builder/ExamBuilderPage";
import { useDeliverySession } from "./hooks/useDeliverySession";
import { useHostContext } from "./hooks/useHostContext";
import { ActivationScreen, shouldShowActivation } from "./activation/ActivationScreen";

export function HostApp() {
  const hostContext = useHostContext();
  const delivery = useDeliverySession(hostContext);
  const [isSchoolConfirmed, setIsSchoolConfirmed] = useState(false);
  const [workspaceMode, setWorkspaceMode] = useState<"sessions" | "builder">("sessions");

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
      <WorkspaceModeTabs mode={workspaceMode} onChange={setWorkspaceMode} />

      {workspaceMode === "sessions" ? (
        <SessionsWorkspace delivery={delivery} />
      ) : (
        <ExamBuilderPage apiBaseUrl={hostContext.apiBaseUrl} />
      )}
    </AppShell>
  );
}
