type QuestionTitleProps = {
  number: number | null;
  text: string;
  required?: boolean;
  id?: string;
};

export function QuestionTitle({ number, text, required, id }: QuestionTitleProps) {
  return (
    <h3 id={id}>
      {number !== null && <span className="student-question-number">{number}</span>}
      {text}
      {required && (
        <span className="student-required-mark" aria-label="pregunta obligatoria"> *</span>
      )}
    </h3>
  );
}
