import { describe, it, expect } from "vitest";
import { documentToReplaceRequest, versionToDocument, normalizeBlockType } from "./mappers";
import type { ExamDocument } from "./exam";
import type { ExamVersion } from "../contracts";

describe("normalizeBlockType", () => {
  it("convierte el índice numérico del enum a string", () => {
    expect(normalizeBlockType(2)).toBe("MultipleChoice");
    expect(normalizeBlockType(3)).toBe("TrueFalse");
    expect(normalizeBlockType(4)).toBe("ShortAnswer");
  });

  it("respeta el valor string", () => {
    expect(normalizeBlockType("TrueFalse")).toBe("TrueFalse");
  });
});

describe("documentToReplaceRequest", () => {
  const doc: ExamDocument = {
    schemaVersion: 1,
    code: "MAT-1",
    title: "T",
    questions: [
      {
        id: "q1",
        type: "single_choice",
        prompt: "¿2+2?",
        required: true,
        score: 2,
        options: [
          { id: "a", label: "4", isCorrect: true },
          { id: "b", label: "5", isCorrect: false }
        ]
      },
      { id: "q2", type: "true_false", prompt: "Primo", required: true, score: 1, correctAnswer: false },
      { id: "q3", type: "free_text", prompt: "Definí", required: false, score: 1 }
    ]
  };

  const request = documentToReplaceRequest(doc);

  it("mapea choice a MultipleChoice con opciones y respuesta correcta", () => {
    const block = request.blocks[0];
    expect(block.blockType).toBe("MultipleChoice");
    expect((block.config as { question: string }).question).toBe("¿2+2?");
    expect((block.config as { options: unknown[] }).options).toHaveLength(2);
    expect(block.correctAnswer).toEqual(["a"]);
    expect(block.scoreValue).toBe(2);
    expect(block.orderIndex).toBe(0);
  });

  it("mapea true_false con respuesta booleana", () => {
    expect(request.blocks[1].blockType).toBe("TrueFalse");
    expect(request.blocks[1].correctAnswer).toBe(false);
  });

  it("mapea free_text sin answer key cuando no hay respuesta modelo", () => {
    expect(request.blocks[2].blockType).toBe("ShortAnswer");
    expect(request.blocks[2].correctAnswer).toBeUndefined();
  });
});

describe("versionToDocument", () => {
  const version: ExamVersion = {
    id: "v1",
    examId: "e1",
    versionNumber: 1,
    schemaVersion: 1,
    status: "Draft",
    metadata: { title: "Título guardado", subject: "Matemática" },
    blocks: [
      {
        id: "b1",
        versionId: "v1",
        orderIndex: 0,
        blockType: 2, // numérico: MultipleChoice (compat sin JsonStringEnumConverter)
        title: "Q",
        config: { question: "¿2+2?", multiple: false, options: [{ value: "a", label: "4" }, { value: "b", label: "5" }] },
        validation: { required: true }
      }
    ],
    answerKeys: [{ id: "k1", blockId: "b1", correctAnswer: ["a"], scoreValue: 3 }],
    assets: []
  };

  const doc = versionToDocument(version, { code: "MAT-1", subject: null, level: null, area: null });

  it("reconstruye metadata y preguntas desde la versión del API", () => {
    expect(doc.title).toBe("Título guardado");
    expect(doc.subject).toBe("Matemática");
    expect(doc.questions).toHaveLength(1);
  });

  it("marca la opción correcta y toma el puntaje del answer key", () => {
    const question = doc.questions[0];
    expect(question.type).toBe("single_choice");
    expect(question.score).toBe(3);
    if (question.type === "single_choice") {
      expect(question.options.find(option => option.id === "a")?.isCorrect).toBe(true);
      expect(question.options.find(option => option.id === "b")?.isCorrect).toBe(false);
    }
  });
});
