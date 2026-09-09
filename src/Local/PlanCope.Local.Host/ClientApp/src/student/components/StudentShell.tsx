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
          <p className="student-eyebrow">Examen local</p>
          <h1>Plan Cope</h1>
        </div>
      </header>
      <div className="student-content">{children}</div>
    </main>
  );
}
