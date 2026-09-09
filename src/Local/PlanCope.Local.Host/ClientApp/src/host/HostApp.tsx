import { useState } from "react";
import { AppShell } from "./components/AppShell";
import { SchoolGate } from "./components/SchoolGate";
import { SessionsWorkspace } from "./components/SessionsWorkspace";
import { useDeliverySession } from "./hooks/useDeliverySession";
import { useHostContext } from "./hooks/useHostContext";

export function HostApp() {
  const hostContext = useHostContext();
  const delivery = useDeliverySession(hostContext);
  const [isSchoolConfirmed, setIsSchoolConfirmed] = useState(false);

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
      <SessionsWorkspace delivery={delivery} />
    </AppShell>
  );
}
