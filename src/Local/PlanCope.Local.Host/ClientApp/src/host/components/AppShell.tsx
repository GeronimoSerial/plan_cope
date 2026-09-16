import { useState } from "react";
import type { ReactNode } from "react";
import { EnrolmentScreen } from "../enrolment/EnrolmentScreen";

type AppShellProps = {
  status: string;
  apiBaseUrl?: string;
  children: ReactNode;
};

export function AppShell({ status, apiBaseUrl, children }: AppShellProps) {
  const [isEnrolmentOpen, setIsEnrolmentOpen] = useState(false);

  return (
    <main className="app-shell">
      <header className="topbar">
        <div className="brand-mark" aria-hidden="true">
          <span />
        </div>
        <div>
          <h1>Plan Cope Local</h1>
          <p>Gestión de sesiones escolares</p>
        </div>
        {apiBaseUrl ? (
          <button type="button" className="enrolment-trigger" onClick={() => setIsEnrolmentOpen(true)}>
            Inscribir equipo
          </button>
        ) : null}
      </header>

      {isEnrolmentOpen && apiBaseUrl ? (
        <div className="enrolment-overlay">
          <EnrolmentScreen apiBaseUrl={apiBaseUrl} onDone={() => setIsEnrolmentOpen(false)} />
          <button type="button" onClick={() => setIsEnrolmentOpen(false)}>
            Cerrar
          </button>
        </div>
      ) : null}

      <div className="workspace">{children}</div>

      <footer className="footer">
        <span className="footer-status" role="status" aria-live="polite">{status}</span>
      </footer>
    </main>
  );
}
