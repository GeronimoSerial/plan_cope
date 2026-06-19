type ExamConfirmationPanelProps = {
  code: string;
};

export function ExamConfirmationPanel({ code }: ExamConfirmationPanelProps) {
  return (
    <section className="student-panel">
      <h2>Examen enviado</h2>
      <p>Codigo de confirmacion:</p>
      <strong className="confirmation-code">{code}</strong>
      <p>Entrega registrada en este equipo. No cierres esta pantalla hasta que el docente lo indique.</p>
    </section>
  );
}
