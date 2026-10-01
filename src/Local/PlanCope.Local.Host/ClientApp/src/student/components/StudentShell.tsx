import type { ReactNode } from "react";

type StudentShellProps = {
  children: ReactNode;
};

export function StudentShell({ children }: StudentShellProps) {
  return (
    <main className="student-page">
      <header className="student-header">
        <div className="institutional-ribbon" aria-hidden="true">
          <span /><span /><span /><span /><span />
        </div>
        <div className="student-signature">
          <img src="/static/logo-educacion-h.svg" alt="Gobierno de Corrientes - Ministerio de Educacion" />
          <div className="student-brand-copy">
            <h1>Plan COPE</h1>
            <p>Evaluación</p>
          </div>
        </div>
      </header>
      <div className="student-content">{children}</div>
    </main>
  );
}
