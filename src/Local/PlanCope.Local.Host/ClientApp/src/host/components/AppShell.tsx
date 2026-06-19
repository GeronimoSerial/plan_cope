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
          <p>Consola institucional de toma en red escolar</p>
        </div>
        <strong>Nodo local</strong>
      </header>

      <div className="workspace">{children}</div>

      <footer className="footer">
        <span>{status}</span>
        <span>API embebida + SQLite local</span>
      </footer>
    </main>
  );
}
