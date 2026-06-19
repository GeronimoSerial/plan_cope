type QuestionTitleProps = {
  number: number | null;
  text: string;
  required?: boolean;
};

export function QuestionTitle({ number, text, required }: QuestionTitleProps) {
  return (
    <h3>
      {number !== null && <span className="student-question-number">{number}</span>}
      {text}
      {required && (
        <span className="student-required-mark" aria-label="pregunta obligatoria"> *</span>
      )}
    </h3>
  );
}
