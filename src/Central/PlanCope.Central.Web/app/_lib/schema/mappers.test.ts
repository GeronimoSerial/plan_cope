import { describe, it, expect } from "vitest";
import { documentToReplaceRequest, versionToDocument, normalizeBlockType, evaluateDocumentReadiness } from "./mappers";
import type { ExamDocument } from "./exam";
import type { ExamVersion } from "../contracts";

describe("normalizeBlockType", () => {
  it("normalizes persisted enum values", () => {
    expect(normalizeBlockType(2)).toBe("MultipleChoice");
    expect(normalizeBlockType(3)).toBe("TrueFalse");
    expect(() => normalizeBlockType(0)).toThrow("Unsupported exam block type");
  });
});

describe("scoring policy block mapping", () => {
  const document: ExamDocument = {
    schemaVersion: 1,
    code: "MAT-1",
    title: "Examen",
    questions: [{
      id: "q1", type: "multiple_choice", scoringPolicy: "ProportionalPlain", prompt: "¿2+2?", required: true, score: 2,
      options: [{ id: "a", label: "4", isCorrect: true }, { id: "b", label: "5", isCorrect: false }]
    }]
  };

  it("writes the question policy into block config", () => {
    const request = documentToReplaceRequest(document);
    expect(request.blocks[0].blockType).toBe("MultipleChoice");
    expect(request.blocks[0].config).toMatchObject({ multiple: true, scoringPolicy: "ProportionalPlain" });
  });

  it("round-trips the question policy and defaults a missing one", () => {
    const version: ExamVersion = {
      id: "v1", examId: "e1", versionNumber: 1, schemaVersion: 1, status: "Draft", metadata: null,
      blocks: [{ id: "b1", versionId: "v1", orderIndex: 0, blockType: "MultipleChoice", title: "Q",
        config: { question: "¿2+2?", multiple: true, scoringPolicy: "ProportionalPlain", options: [{ value: "a", label: "4" }, { value: "b", label: "5" }] }, validation: { required: true } }],
      answerKeys: [{ id: "k1", blockId: "b1", correctAnswer: ["a"], scoreValue: 3 }], assets: []
    };
    const exam = { code: "MAT-1", title: "Examen", subject: null, level: null, area: null };
    const question = versionToDocument(version, exam).questions[0];
    expect(question.type === "multiple_choice" && question.scoringPolicy).toBe("ProportionalPlain");
    const withoutPolicy = { ...version, blocks: [{ ...version.blocks[0], config: { ...version.blocks[0].config, scoringPolicy: undefined } }] };
    const defaulted = versionToDocument(withoutPolicy, exam).questions[0];
    expect(defaulted.type === "multiple_choice" && defaulted.scoringPolicy).toBe("AllOrNothing");
  });
});

describe("readiness", () => {
  it("requires at least one question and does not require an exam-level scoring policy", () => {
    expect(evaluateDocumentReadiness({ schemaVersion: 1, code: "X", title: "T", questions: [] })).toEqual({ canPublish: false, blockedReason: "no_blocks" });
    const doc: ExamDocument = { schemaVersion: 1, code: "X", title: "T", questions: [{
      id: "q", type: "multiple_choice", scoringPolicy: "AllOrNothing", prompt: "Q", required: true, score: 1,
      options: [{ id: "a", label: "A", isCorrect: true }, { id: "b", label: "B", isCorrect: false }]
    }] };
    expect(evaluateDocumentReadiness(doc)).toEqual({ canPublish: true, blockedReason: null });
  });
});
