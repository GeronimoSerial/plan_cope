import type { ScoringPolicy } from "../schema/exam";

// Pure scoring math used by the policy picker's worked example. The semantics mirror
// PlanCope.Shared.Grading.ScoringPolicy and the note in docs/central/exam-publishing-contract.md.

export interface ScoreExample {
  correctTotal: number;
  correctMarked: number;
  incorrectMarked: number;
}

// Reference case shown in the picker: a question with 2 correct answers where the student
// marks one correct and one incorrect option.
export const referenceExample: ScoreExample = {
  correctTotal: 2,
  correctMarked: 1,
  incorrectMarked: 1
};

export function exampleScore(policy: ScoringPolicy, marks: ScoreExample): number {
  const { correctTotal, correctMarked, incorrectMarked } = marks;
  if (correctTotal <= 0) {
    return 0;
  }

  switch (policy) {
    case "AllOrNothing":
      // Full credit only when the marked set matches the key exactly (nothing missing, nothing extra).
      return correctMarked === correctTotal && incorrectMarked === 0 ? 1 : 0;
    case "ProportionalPenalised":
      return Math.max(0, (correctMarked - incorrectMarked) / correctTotal);
    case "ProportionalPlain":
      return correctMarked / correctTotal;
  }
}

// es-AR decimal formatting: 0.5 -> "0,5", 0 -> "0", 1 -> "1".
export function formatScore(value: number): string {
  return new Intl.NumberFormat("es-AR", { maximumFractionDigits: 2 }).format(value);
}
