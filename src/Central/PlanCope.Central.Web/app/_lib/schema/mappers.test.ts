import { describe, it, expect } from "vitest";
import {
  documentToReplaceRequest,
  versionToDocument,
  normalizeBlockType,
  documentNeedsScoringPolicy,
  evaluateDocumentReadiness,
  mergeDocumentReadiness,
  type DocumentReadiness,
  type ServerReadiness
} from "./mappers";
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
    scoringPolicy: "AllOrNothing",
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
      { id: "q3", type: "free_text", prompt: "Definí", required: false, score: 1 },
      { id: "q4", type: "text_block", prompt: "Leé con atención.", help: "Intro" },
      { id: "q5", type: "image_block", prompt: "Diagrama", assetId: "asset-1" }
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

  it("mapea text_block a Text con config.content y sin puntaje", () => {
    const block = request.blocks[3];
    expect(block.blockType).toBe("Text");
    expect(block.config).toMatchObject({ content: "Leé con atención.", help: "Intro" });
    expect(block.validation).toEqual({ required: false });
    expect(block.scoreValue).toBe(0);
    expect(block.correctAnswer).toBeUndefined();
  });

  it("mapea image_block a Image con config.assetId y caption", () => {
    const block = request.blocks[4];
    expect(block.blockType).toBe("Image");
    expect(block.config).toMatchObject({ assetId: "asset-1", caption: "Diagrama" });
    expect(block.validation).toEqual({ required: false });
    expect(block.scoreValue).toBe(0);
  });

  it("incluye la política de puntaje en el request", () => {
    expect(request.scoringPolicy).toBe("AllOrNothing");
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
    scoringPolicy: "ProportionalPlain",
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

  const doc = versionToDocument(version, { code: "MAT-1", title: "Examen de prueba", subject: null, level: null, area: null });

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

  it("reconstruye la política de puntaje guardada", () => {
    expect(doc.scoringPolicy).toBe("ProportionalPlain");
  });

  it("redondea bloques Text e Image a text_block/image_block (no a free_text)", () => {
    const withContent: ExamVersion = {
      ...version,
      blocks: [
        ...version.blocks,
        {
          id: "b2",
          versionId: "v1",
          orderIndex: 1,
          blockType: "Text",
          title: "T",
          config: { content: "Leé con atención.", help: "Intro" },
          validation: { required: false }
        },
        {
          id: "b3",
          versionId: "v1",
          orderIndex: 2,
          blockType: "Image",
          title: "I",
          config: { assetId: "asset-1", caption: "Diagrama" },
          validation: { required: false }
        }
      ]
    };

    const mapped = versionToDocument(withContent, { code: "MAT-1", title: "Examen de prueba", subject: null, level: null, area: null });

    const text = mapped.questions[1];
    expect(text.type).toBe("text_block");
    if (text.type === "text_block") {
      expect(text.prompt).toBe("Leé con atención.");
      expect(text.help).toBe("Intro");
    }

    const image = mapped.questions[2];
    expect(image.type).toBe("image_block");
    if (image.type === "image_block") {
      expect(image.assetId).toBe("asset-1");
      expect(image.prompt).toBe("Diagrama");
    }
  });
});

describe("evaluateDocumentReadiness", () => {
  const choice = (type: "single_choice" | "multiple_choice"): ExamDocument["questions"][number] => ({
    id: "q",
    type,
    prompt: "¿?",
    required: true,
    score: 1,
    options: [
      { id: "a", label: "A", isCorrect: true },
      { id: "b", label: "B", isCorrect: false }
    ]
  });

  const doc = (questions: ExamDocument["questions"], scoringPolicy?: ExamDocument["scoringPolicy"]): ExamDocument => ({
    schemaVersion: 1,
    code: "MAT-1",
    title: "T",
    scoringPolicy,
    questions
  });

  it("bloquea sin preguntas", () => {
    expect(evaluateDocumentReadiness(doc([]))).toEqual({ canPublish: false, blockedReason: "no_blocks" });
  });

  it("exige regla de puntaje para cualquier bloque MultipleChoice (incluye opción única)", () => {
    expect(documentNeedsScoringPolicy(doc([choice("multiple_choice")]))).toBe(true);
    // single_choice también se persiste como bloque MultipleChoice (config.multiple=false).
    expect(documentNeedsScoringPolicy(doc([choice("single_choice")]))).toBe(true);
    expect(evaluateDocumentReadiness(doc([choice("multiple_choice")]))).toEqual({
      canPublish: false,
      blockedReason: "scoring_policy_required"
    });
    expect(evaluateDocumentReadiness(doc([choice("multiple_choice")], "AllOrNothing"))).toEqual({
      canPublish: true,
      blockedReason: null
    });
    expect(evaluateDocumentReadiness(doc([choice("single_choice")]))).toEqual({
      canPublish: false,
      blockedReason: "scoring_policy_required"
    });
  });

  it("no exige regla cuando solo hay bloques sin puntaje parcial", () => {
    expect(documentNeedsScoringPolicy(doc([{ id: "q", type: "true_false", prompt: "¿?", required: true, score: 1, correctAnswer: true }]))).toBe(false);
    expect(evaluateDocumentReadiness(doc([{ id: "q", type: "text_block", prompt: "Leé." }]))).toEqual({
      canPublish: true,
      blockedReason: null
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
    scoringPolicy: null,
    blocks: [],
    answerKeys: [],
    assets: []
  };

  const summary = {
    code: "EXA-2026-01",
    title: "Matemática · Primer Año",
    subject: "Números",
    level: "Secundario",
    area: "Matemática"
  };

  it("usa el ExamSummary como fallback cuando la versión no tiene metadata (create -> builder)", () => {
    const doc = versionToDocument(emptyVersion, summary);
    expect(doc.title).toBe("Matemática · Primer Año");
    expect(doc.code).toBe("EXA-2026-01");
    expect(doc.subject).toBe("Números");
    expect(doc.level).toBe("Secundario");
    expect(doc.area).toBe("Matemática");
  });

  it("ignora metadata vacío o en blanco y cae al fallback", () => {
    const doc = versionToDocument({ ...emptyVersion, metadata: { title: "   " } }, summary);
    expect(doc.title).toBe("Matemática · Primer Año");
  });

  it("la metadata gana sobre el fallback cuando trae valor", () => {
    const doc = versionToDocument({ ...emptyVersion, metadata: { title: "Título guardado", level: "Primario" } }, summary);
    expect(doc.title).toBe("Título guardado");
    expect(doc.level).toBe("Primario");
    // Sin metadata de subject/area, siguen cayendo al summary.
    expect(doc.subject).toBe("Números");
    expect(doc.area).toBe("Matemática");
  });
});

describe("mergeDocumentReadiness", () => {
  it("con cambios locales manda la evaluación en memoria", () => {
    const local: DocumentReadiness = { canPublish: true, blockedReason: null };
    const server: ServerReadiness = { canPublish: false, blockedReason: "no_blocks" };
    expect(mergeDocumentReadiness(local, server, true)).toEqual({ canPublish: true, blockedReason: null });
  });

  it("sin cambios usa el servidor (evita usar un canPublish viejo tras guardar)", () => {
    const local: DocumentReadiness = { canPublish: false, blockedReason: "no_blocks" };
    const server: ServerReadiness = { canPublish: true, blockedReason: null };
    expect(mergeDocumentReadiness(local, server, false)).toEqual({ canPublish: true, blockedReason: null });
  });

  it("sin cambios respeta el motivo de bloqueo del servidor", () => {
    const local: DocumentReadiness = { canPublish: true, blockedReason: null };
    const server: ServerReadiness = { canPublish: false, blockedReason: "scoring_policy_required" };
    expect(mergeDocumentReadiness(local, server, false)).toEqual({
      canPublish: false,
      blockedReason: "scoring_policy_required"
    });
  });

  it("un motivo desconocido del servidor deshabilita y cae a un motivo genérico", () => {
    const local: DocumentReadiness = { canPublish: true, blockedReason: null };
    // already_published is a real server reason this client does not render locally...
    expect(mergeDocumentReadiness(local, { canPublish: false, blockedReason: "already_published" }, false)).toEqual({
      canPublish: false,
      blockedReason: "unknown"
    });
    // ...and an entirely future reason must behave the same way.
    const future = "future_reason" as unknown as ServerReadiness["blockedReason"];
    expect(mergeDocumentReadiness(local, { canPublish: false, blockedReason: future }, false)).toEqual({
      canPublish: false,
      blockedReason: "unknown"
    });
  });
});
