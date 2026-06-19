import { describe, it, expect } from "vitest";
import { examDocumentSchema, type ExamDocument } from "./exam";

function baseDoc(questions: ExamDocument["questions"]): ExamDocument {
  return {
    schemaVersion: 1,
    code: "MAT-2026-01",
    title: "Examen de prueba",
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

  it("acepta verdadero/falso y texto libre", () => {
    const result = examDocumentSchema.safeParse(
      baseDoc([
        { id: "q1", type: "true_false", prompt: "El 7 es primo", required: true, score: 1, correctAnswer: true },
        { id: "q2", type: "free_text", prompt: "Definí par", required: false, score: 2 }
      ])
    );
    expect(result.success).toBe(true);
  });
});
