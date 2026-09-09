import type { ReactNode } from "react";

type AppShellProps = {
  status: string;
  children: ReactNode;
};

export function AppShell({ status, children }: AppShellProps) {
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
      </header>

      <div className="workspace">{children}</div>

      <footer className="footer">
        <span className="footer-status" role="status" aria-live="polite">{status}</span>
      </footer>
    </main>
  );
}
