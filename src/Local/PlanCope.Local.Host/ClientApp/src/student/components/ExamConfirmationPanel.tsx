import { useState } from "react";

type ExamConfirmationPanelProps = {
  code: string;
  submittedAt?: string | null;
};

export function ExamConfirmationPanel({ code, submittedAt }: ExamConfirmationPanelProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  };

  const formattedTime = submittedAt ? formatSubmittedAt(submittedAt) : null;

  return (
    <section className="student-gate">
      <div className="student-card student-confirmation">
        <div className="student-success-icon" aria-hidden="true">
          ✓
        </div>
        <h2>Examen enviado</h2>
        <p className="student-confirmation-copy">
          Tu entrega se registro en este equipo. Conserva el codigo de confirmacion.
        </p>
        <div className="student-confirmation-code-box">
          <span className="student-confirmation-label">Codigo de confirmacion</span>
          <strong className="confirmation-code">{code}</strong>
          <button type="button" className="student-copy-button" onClick={handleCopy}>
            {copied ? "Copiado" : "Copiar codigo"}
          </button>
        </div>
        {formattedTime && <p className="student-confirmation-time">Entregado a las {formattedTime}</p>}
        <div className="student-alert" role="alert">
          <span className="student-alert-icon" aria-hidden="true">
            !
          </span>
          <span>No cierres esta pantalla hasta que el docente lo indique.</span>
        </div>
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
