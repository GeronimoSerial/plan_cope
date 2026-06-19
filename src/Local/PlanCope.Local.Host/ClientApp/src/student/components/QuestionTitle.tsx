type QuestionTitleProps = {
  number: number | null;
  text: string;
};

export function QuestionTitle({ number, text }: QuestionTitleProps) {
  return (
    <h3>
      {number !== null && <span className="student-question-number">{number}</span>}
      {text}
    </h3>
  );
}
