import { describe, it, expect } from "vitest";
import {
  documentToReplaceRequest,
  versionToDocument,
  normalizeBlockType,
  evaluateDocumentReadiness,
  mergeDocumentReadiness,
  type DocumentReadiness,
  type ServerReadiness
} from "./mappers";
import type { ExamDocument } from "./exam";
import type { ExamVersion } from "../contracts";

describe("normalizeBlockType", () => {
  it("normalizes persisted enum values", () => {
    expect(normalizeBlockType(2)).toBe("MultipleChoice");
    expect(normalizeBlockType(3)).toBe("TrueFalse");
    expect(() => normalizeBlockType(0)).toThrow("Unsupported exam block type");
    expect(() => normalizeBlockType(1)).toThrow("Unsupported exam block type");
    expect(() => normalizeBlockType(4)).toThrow("Unsupported exam block type");
    expect(() => normalizeBlockType("Essay")).toThrow("Unsupported exam block type");
  });

  it("respects string values", () => {
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
        type: "multiple_choice",
        scoringPolicy: "AllOrNothing",
        prompt: "¿2+2?",
        required: true,
        score: 2,
        options: [
          { id: "a", label: "4", isCorrect: true },
          { id: "b", label: "5", isCorrect: false }
        ]
      },
      { id: "q2", type: "true_false", prompt: "Primo", required: true, score: 1, correctAnswer: false }
    ]
  };

  const request = documentToReplaceRequest(doc);

  it("maps multiple choice to a block with options, policy, and correct answer", () => {
    const block = request.blocks[0];
    expect(block.blockType).toBe("MultipleChoice");
    expect(block.config).toMatchObject({
      question: "¿2+2?",
      multiple: true,
      scoringPolicy: "AllOrNothing",
      options: [{ value: "a", label: "4" }, { value: "b", label: "5" }]
    });
    expect(block.correctAnswer).toEqual(["a"]);
    expect(block.scoreValue).toBe(2);
    expect(block.orderIndex).toBe(0);
  });

  it("maps true_false with a boolean answer", () => {
    expect(request.blocks[1].blockType).toBe("TrueFalse");
    expect(request.blocks[1].correctAnswer).toBe(false);
  });
});

describe("scoring policy block mapping", () => {
  const document: ExamDocument = {
    schemaVersion: 1,
    code: "MAT-1",
    title: "Examen",
    questions: [{
      id: "q1",
      type: "multiple_choice",
      scoringPolicy: "ProportionalPlain",
      prompt: "¿2+2?",
      required: true,
      score: 2,
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
    const exam = { code: "MAT-1", title: "Examen", subject: null, courses: ["primaria-1"], area: null };
    const question = versionToDocument(version, exam).questions[0];
    expect(question.type === "multiple_choice" && question.scoringPolicy).toBe("ProportionalPlain");
    const withoutPolicy = { ...version, blocks: [{ ...version.blocks[0], config: { ...version.blocks[0].config, scoringPolicy: undefined } }] };
    const defaulted = versionToDocument(withoutPolicy, exam).questions[0];
    expect(defaulted.type === "multiple_choice" && defaulted.scoringPolicy).toBe("AllOrNothing");
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
        blockType: 2, // Numeric enum value for MultipleChoice compatibility.
        title: "Q",
        config: { question: "¿2+2?", multiple: false, options: [{ value: "a", label: "4" }, { value: "b", label: "5" }] },
        validation: { required: true }
      }
    ],
    answerKeys: [{ id: "k1", blockId: "b1", correctAnswer: ["a"], scoreValue: 3 }],
    assets: []
  };

  const doc = versionToDocument(version, { code: "MAT-1", title: "Examen de prueba", subject: null, courses: ["primaria-1"], area: null });

  it("rebuilds metadata and questions from the API version", () => {
    expect(doc.title).toBe("Título guardado");
    expect(doc.subject).toBe("Matemática");
    expect(doc.questions).toHaveLength(1);
  });

  it("marks the correct option and takes the score from the answer key", () => {
    const question = doc.questions[0];
    expect(question.type).toBe("single_choice");
    expect(question.score).toBe(3);
    if (question.type === "single_choice") {
      expect(question.options.find(option => option.id === "a")?.isCorrect).toBe(true);
      expect(question.options.find(option => option.id === "b")?.isCorrect).toBe(false);
    }
  });

  it("round-trips a question image asset reference", () => {
    const withImage = {
      ...version,
      blocks: [{ ...version.blocks[0], config: { ...version.blocks[0].config, imageAssetId: "asset-42" } }]
    };
    const question = versionToDocument(withImage, { code: "MAT-1", title: "Examen", subject: null, courses: ["primaria-1"], area: null }).questions[0];
    expect(question.imageAssetId).toBe("asset-42");
    expect(documentToReplaceRequest({ ...doc, questions: [question] }).blocks[0].config.imageAssetId).toBe("asset-42");
  });
});

describe("evaluateDocumentReadiness", () => {
  it("blocks when there are no questions", () => {
    expect(evaluateDocumentReadiness({ schemaVersion: 1, code: "MAT-1", title: "T", courses: ["primaria-1"], questions: [] })).toEqual({
      canPublish: false,
      blockedReason: "no_blocks"
    });
  });
});

describe("versionToDocument fallbacks", () => {
  const emptyVersion: ExamVersion = {
    id: "v1",
    examId: "e1",
    versionNumber: 1,
    schemaVersion: 1,
    status: "Draft",
    metadata: null,
    blocks: [],
    answerKeys: [],
    assets: []
  };

  const summary = {
    code: "EXA-2026-01",
    title: "Matemática · Primer Año",
    subject: "Números",
    courses: ["secundaria-2"],
    area: "Matemática"
  };

  it("uses ExamSummary as fallback when the version has no metadata", () => {
    const doc = versionToDocument(emptyVersion, summary);
    expect(doc.title).toBe("Matemática · Primer Año");
    expect(doc.code).toBe("EXA-2026-01");
    expect(doc.subject).toBe("Números");
    expect(doc.courses).toEqual(["secundaria-2"]);
    expect(doc.area).toBe("Matemática");
  });

  it("ignores empty or blank metadata and uses the fallback", () => {
    const doc = versionToDocument({ ...emptyVersion, metadata: { title: "   " } }, summary);
    expect(doc.title).toBe("Matemática · Primer Año");
  });

  it("uses exam summary courses and area when version metadata is stale", () => {
    const doc = versionToDocument({
      ...emptyVersion,
      metadata: { title: "Título guardado", courses: ["primaria-2"], area: "Área anterior" }
    }, summary);
    expect(doc.title).toBe("Título guardado");
    expect(doc.courses).toEqual(["secundaria-2"]);
    expect(doc.subject).toBe("Números");
    expect(doc.area).toBe("Matemática");
  });
});

describe("mergeDocumentReadiness", () => {
  it("uses the in-memory evaluation when there are local changes", () => {
    const local: DocumentReadiness = { canPublish: true, blockedReason: null };
    const server: ServerReadiness = { canPublish: false, blockedReason: "no_blocks" };
    expect(mergeDocumentReadiness(local, server, true)).toEqual({ canPublish: true, blockedReason: null });
  });

  it("uses the server when there are no changes", () => {
    const local: DocumentReadiness = { canPublish: false, blockedReason: "no_blocks" };
    const server: ServerReadiness = { canPublish: true, blockedReason: null };
    expect(mergeDocumentReadiness(local, server, false)).toEqual({ canPublish: true, blockedReason: null });
  });

  it("preserves the server's blocking reason when there are no changes", () => {
    const local: DocumentReadiness = { canPublish: true, blockedReason: null };
    const server: ServerReadiness = { canPublish: false, blockedReason: "no_blocks" };
    expect(mergeDocumentReadiness(local, server, false)).toEqual({ canPublish: false, blockedReason: "no_blocks" });
  });

  it("disables publishing for an unknown server reason and uses a generic reason", () => {
    const local: DocumentReadiness = { canPublish: true, blockedReason: null };
    expect(mergeDocumentReadiness(local, { canPublish: false, blockedReason: "already_published" }, false)).toEqual({
      canPublish: false,
      blockedReason: "unknown"
    });
    const future = "future_reason" as unknown as ServerReadiness["blockedReason"];
    expect(mergeDocumentReadiness(local, { canPublish: false, blockedReason: future }, false)).toEqual({
      canPublish: false,
      blockedReason: "unknown"
    });
  });
});
