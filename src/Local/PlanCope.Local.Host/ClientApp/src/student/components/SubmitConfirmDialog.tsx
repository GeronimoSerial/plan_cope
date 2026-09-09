import { useEffect, useRef } from "react";

type SubmitConfirmDialogProps = {
  answered: number;
  total: number;
  missingRequiredCount: number;
  onConfirm: () => void;
  onCancel: () => void;
};

export function SubmitConfirmDialog({
  answered,
  total,
  missingRequiredCount,
  onConfirm,
  onCancel
}: SubmitConfirmDialogProps) {
  const hasMissing = missingRequiredCount > 0;
  const cancelButtonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    cancelButtonRef.current?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        event.preventDefault();
        onCancel();
      }
    };

    document.addEventListener("keydown", handleKeyDown);
    return () => document.removeEventListener("keydown", handleKeyDown);
  }, [onCancel]);

  return (
    <div
      className="student-modal-backdrop"
      role="dialog"
      aria-modal="true"
      aria-labelledby="submit-dialog-title"
      aria-describedby="submit-dialog-description"
      onClick={onCancel}
    >
      <div className="student-modal" onClick={event => event.stopPropagation()}>
        <h2 id="submit-dialog-title">Confirmá el envío</h2>
        <p id="submit-dialog-description" className="student-modal-intro">
          Revisá tus respuestas. Después del envío no vas a poder modificarlas.
        </p>
        <div className="student-modal-summary">
          <div>
            <span>Respondidas</span>
            <strong>
              {answered} / {total}
            </strong>
          </div>
          <div className={hasMissing ? "student-modal-missing" : ""}>
            <span>Obligatorias sin responder</span>
            <strong>{missingRequiredCount}</strong>
          </div>
        </div>
        {hasMissing && (
          <p className="student-modal-warning" role="alert">
            Faltan {missingRequiredCount} preguntas obligatorias. Completalas antes de enviar.
          </p>
        )}
        <div className="student-modal-actions">
          <button ref={cancelButtonRef} type="button" className="button button-secondary" onClick={onCancel}>
            Volver
          </button>
          <button
            type="button"
            className="button button-primary"
            disabled={hasMissing}
            onClick={onConfirm}
          >
            Enviar examen
          </button>
        </div>
      </div>
    </div>
  );
}
