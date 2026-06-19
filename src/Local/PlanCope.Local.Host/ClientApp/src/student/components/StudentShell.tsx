import type { ReactNode } from "react";

type StudentShellProps = {
  children: ReactNode;
};

export function StudentShell({ children }: StudentShellProps) {
  return (
    <main className="student-page">
      <header className="student-topbar">
        <div className="student-brand-mark" aria-hidden="true">
          <span />
        </div>
        <div>
          <p className="student-eyebrow">Toma local de examen</p>
          <h1>Plan Cope</h1>
          <p>Funciona dentro de la red de la escuela.</p>
        </div>
      </header>
      <div className="student-content">{children}</div>
      <footer className="student-footer">
        <span>Nodo local</span>
        <span>API embebida + SQLite local</span>
      </footer>
    </main>
  );
}
