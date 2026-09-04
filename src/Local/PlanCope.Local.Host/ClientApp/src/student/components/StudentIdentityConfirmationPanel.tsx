import { ActionButton } from "../../shared/ui";
import type { ResolvedStudent } from "../types";

type StudentIdentityConfirmationPanelProps = {
  student: ResolvedStudent;
  isBusy: boolean;
  error: string;
  onConfirm: () => void;
  onCorrect: () => void;
};

export function StudentIdentityConfirmationPanel({
  student,
  isBusy,
  error,
  onConfirm,
  onCorrect
}: StudentIdentityConfirmationPanelProps) {
  return (
    <section className="student-gate" aria-labelledby="identity-confirmation-title">
      <div className="student-card student-identity-card">
        <p className="eyebrow">Confirmación de identidad</p>
        <h2 id="identity-confirmation-title">¿Sos {student.firstName}?</h2>
        <div className="student-identity-summary" aria-live="polite">
          <strong>{student.displayName}</strong>
          <span>DNI {student.maskedDocument}</span>
        </div>
        <p className="student-card-copy">
          Verificá que estos datos sean tuyos. El examen comienza recién cuando confirmes.
        </p>
        <div className="student-identity-actions">
          <ActionButton disabled={isBusy} onClick={onConfirm}>
            {isBusy ? "Iniciando..." : "Sí, soy yo"}
          </ActionButton>
          <ActionButton variant="secondary" disabled={isBusy} onClick={onCorrect}>
            Volver y corregir
          </ActionButton>
        </div>
        {error && <p className="error-banner" role="alert">{error}</p>}
      </div>
    </section>
  );
}
