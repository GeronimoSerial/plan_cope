type ExamConfirmationPanelProps = {
  submittedAt?: string | null;
};

export function ExamConfirmationPanel({ submittedAt }: ExamConfirmationPanelProps) {
  const formattedTime = submittedAt ? formatSubmittedAt(submittedAt) : null;

  return (
    <section className="student-gate">
      <div className="student-card student-confirmation">
        <div className="student-success-icon" aria-hidden="true">
          ✓
        </div>
        <h2>Examen enviado</h2>
        <p className="student-confirmation-copy">
          Tu entrega se registró. Ya podés cerrar esta ventana o avisarle a tu docente.
        </p>
        {formattedTime && <p className="student-confirmation-time">Entregado a las {formattedTime}</p>}
      </div>
    </section>
  );
}

function formatSubmittedAt(iso: string): string | null {
  try {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) {
      return null;
    }
    return date.toLocaleTimeString("es-AR", { hour: "2-digit", minute: "2-digit" });
  } catch {
    return null;
  }
}
