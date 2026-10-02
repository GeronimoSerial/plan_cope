import { describe, it, expect } from "vitest";
import {
  examDocumentSchema,
  createExamSchema,
  scoringPolicies,
  scoringPolicyExplanations,
  scoringPolicyWarnings,
  type ExamDocument
} from "./exam";

function baseDoc(questions: ExamDocument["questions"]): ExamDocument {
  return {
    schemaVersion: 1,
    code: "MAT-2026-01",
    title: "Examen de prueba",
    courses: ["primaria-1"],
    questions
  };
}

describe("examDocumentSchema", () => {
  it("acepta un documento válido", () => {
    const doc = baseDoc([
      {
        id: "q1",
        type: "single_choice",
        prompt: "¿2 + 2?",
        required: true,
        score: 1,
        options: [
          { id: "a", label: "4", isCorrect: true },
          { id: "b", label: "5", isCorrect: false }
        ]
      }
    ]);
    expect(examDocumentSchema.safeParse(doc).success).toBe(true);
  });

  it("rechaza un examen sin preguntas", () => {
    const result = examDocumentSchema.safeParse(baseDoc([]));
    expect(result.success).toBe(false);
  });

  it("requires at least one known course", () => {
    const valid = baseDoc([]);
    expect(examDocumentSchema.safeParse({ ...valid, courses: [] }).success).toBe(false);
    expect(examDocumentSchema.safeParse({ ...valid, courses: ["curso-inexistente"] }).success).toBe(false);
  });

  it("rechaza opción única con dos respuestas correctas", () => {
    const result = examDocumentSchema.safeParse(
      baseDoc([
        {
          id: "q1",
          type: "single_choice",
          prompt: "Elegí",
          required: true,
          score: 1,
          options: [
            { id: "a", label: "A", isCorrect: true },
            { id: "b", label: "B", isCorrect: true }
          ]
        }
      ])
    );
    expect(result.success).toBe(false);
  });

  it("rechaza choice sin respuesta correcta", () => {
    const result = examDocumentSchema.safeParse(
      baseDoc([
        {
          id: "q1",
          type: "multiple_choice",
          prompt: "Elegí",
          required: true,
          score: 1,
          options: [
            { id: "a", label: "A", isCorrect: false },
            { id: "b", label: "B", isCorrect: false }
          ]
        }
      ])
    );
    expect(result.success).toBe(false);
  });

  it("rechaza choice con menos de dos opciones", () => {
    const result = examDocumentSchema.safeParse(
      baseDoc([
        {
          id: "q1",
          type: "single_choice",
          prompt: "Elegí",
          required: true,
          score: 1,
          options: [{ id: "a", label: "A", isCorrect: true }]
        }
      ])
    );
    expect(result.success).toBe(false);
  });

  it("acepta verdadero/falso", () => {
    const result = examDocumentSchema.safeParse(
      baseDoc([
        { id: "q1", type: "true_false", prompt: "El 7 es primo", required: true, score: 1, correctAnswer: true }
      ])
    );
    expect(result.success).toBe(true);
  });

  it.each(["free_text", "text_block", "image_block"])("rechaza el tipo eliminado %s", type => {
    expect(examDocumentSchema.safeParse(baseDoc([{ id: "q", type, prompt: "Pregunta" }])).success).toBe(false);
  });
});

describe("createExamSchema", () => {
  it("acepta altas sin código y sigue aceptando códigos legacy", () => {
    const base = { title: "Examen", courses: ["primaria-1"] };
    expect(createExamSchema.safeParse(base).success).toBe(true);
    expect(createExamSchema.safeParse({ ...base, code: null }).success).toBe(true);
    expect(createExamSchema.safeParse({ ...base, code: "LEGACY-01" }).success).toBe(true);
  });

  it("valida longitud y formato de un código legacy proporcionado", () => {
    expect(createExamSchema.safeParse({ title: "Examen", courses: ["primaria-1"], code: "" }).success).toBe(false);
    expect(createExamSchema.safeParse({ title: "Examen", courses: ["primaria-1"], code: "A".repeat(65) }).success).toBe(false);
  });
});

describe("scoringPolicy copy", () => {
  it("explica cada política en una sola oración", () => {
    for (const policy of scoringPolicies) {
      const explanation = scoringPolicyExplanations[policy];
      expect(explanation.length).toBeGreaterThan(0);
      expect(explanation.includes(".") && explanation.indexOf(".") === explanation.length - 1).toBe(true);
    }
  });

  it("solo advierte sobre el puntaje completo en Proporcional simple", () => {
    expect(scoringPolicyWarnings.ProportionalPlain).toBe("Marcar todas las opciones da el puntaje completo.");
    expect(scoringPolicyWarnings.AllOrNothing).toBeUndefined();
    expect(scoringPolicyWarnings.ProportionalPenalised).toBeUndefined();
  });
});
