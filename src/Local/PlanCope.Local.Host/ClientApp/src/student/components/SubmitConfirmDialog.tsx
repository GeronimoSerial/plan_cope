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

  return (
    <div className="student-modal-backdrop" role="dialog" aria-modal="true" onClick={onCancel}>
      <div className="student-modal" onClick={event => event.stopPropagation()}>
        <h2>Confirmar envio</h2>
        <p className="student-modal-intro">
          Revisa tu estado antes de enviar. Una vez enviado no podras modificar tus respuestas.
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
          <p className="student-modal-warning">
            Tienes {missingRequiredCount} pregunta(s) obligatoria(s) sin responder. Completa todas las
            obligatorias para poder enviar el examen.
          </p>
        )}
        <div className="student-modal-actions">
          <button type="button" className="button button-secondary" onClick={onCancel}>
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
