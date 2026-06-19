import {
  blockTypes,
  type BlockType,
  type DocumentBlock,
  type ExamVersion,
  type ReplaceExamDocumentRequest,
  type ExamSummary
} from "../contracts";
import type { ExamDocument, Question, ExamOption } from "./exam";

// ============================================================
// Anti-corruption layer: traduce entre el schema canonico (fuente
// de verdad del builder) y los contratos del Central API.
// ============================================================

export function newId(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }
  return `id-${Math.random().toString(36).slice(2)}-${Date.now()}`;
}

// El enum BlockType puede llegar como string (con JsonStringEnumConverter)
// o como numero (orden del enum) por compatibilidad.
export function normalizeBlockType(value: BlockType | number | string): BlockType {
  if (typeof value === "number") {
    return blockTypes[value] ?? "Text";
  }
  return (blockTypes as readonly string[]).includes(value) ? (value as BlockType) : "Text";
}

// ---------- Canonico -> API (guardar) ----------
export function documentToReplaceRequest(document: ExamDocument): ReplaceExamDocumentRequest {
  const blocks: DocumentBlock[] = document.questions.map((question, index) =>
    questionToBlock(question, index)
  );

  return {
    metadata: {
      title: document.title,
      description: document.description ?? null,
      subject: document.subject ?? null,
      level: document.level ?? null,
      area: document.area ?? null
    },
    blocks
  };
}

function questionToBlock(question: Question, orderIndex: number): DocumentBlock {
  const base = {
    orderIndex,
    title: question.prompt.slice(0, 120),
    description: question.help ?? null,
    validation: { required: question.required },
    scoreValue: question.score
  };

  switch (question.type) {
    case "single_choice":
    case "multiple_choice":
      return {
        ...base,
        blockType: "MultipleChoice",
        config: {
          question: question.prompt,
          help: question.help ?? null,
          multiple: question.type === "multiple_choice",
          options: question.options.map(option => ({ value: option.id, label: option.label }))
        },
        correctAnswer: question.options.filter(option => option.isCorrect).map(option => option.id)
      };
    case "true_false":
      return {
        ...base,
        blockType: "TrueFalse",
        config: { question: question.prompt, help: question.help ?? null },
        correctAnswer: question.correctAnswer
      };
    case "free_text":
      return {
        ...base,
        blockType: "ShortAnswer",
        config: {
          prompt: question.prompt,
          help: question.help ?? null,
          maxLength: question.maxLength ?? null
        },
        // Solo enviamos answer key si hay respuesta modelo.
        correctAnswer: question.sampleAnswer ? question.sampleAnswer : undefined
      };
  }
}

// ---------- API -> Canonico (cargar en el builder) ----------
export function versionToDocument(version: ExamVersion, exam: Pick<ExamSummary, "code" | "subject" | "level" | "area">): ExamDocument {
  const answerByBlock = new Map(version.answerKeys?.map(key => [key.blockId, key]) ?? []);
  const metadata = (version.metadata ?? {}) as Record<string, unknown>;

  const questions: Question[] = [...(version.blocks ?? [])]
    .sort((a, b) => a.orderIndex - b.orderIndex)
    .map(block => {
      const type = normalizeBlockType(block.blockType);
      const config = (block.config ?? {}) as Record<string, unknown>;
      const validation = (block.validation ?? {}) as Record<string, unknown>;
      const answer = answerByBlock.get(block.id);
      const required = typeof validation.required === "boolean" ? validation.required : true;
      const score = typeof answer?.scoreValue === "number" ? answer.scoreValue : 1;
      const help = typeof config.help === "string" ? config.help : undefined;

      if (type === "MultipleChoice") {
        const multiple = config.multiple === true;
        const rawOptions = Array.isArray(config.options) ? (config.options as Array<Record<string, unknown>>) : [];
        const correctIds = new Set(Array.isArray(answer?.correctAnswer) ? (answer?.correctAnswer as unknown[]).map(String) : []);
        const options: ExamOption[] = rawOptions.map(option => {
          const id = String(option.value ?? newId());
          return {
            id,
            label: String(option.label ?? ""),
            isCorrect: correctIds.has(id)
          };
        });
        return {
          id: block.id,
          type: multiple ? "multiple_choice" : "single_choice",
          prompt: String(config.question ?? block.title ?? ""),
          help,
          required,
          score,
          options: options.length >= 2 ? options : [...options, ...emptyOptions(2 - options.length)]
        };
      }

      if (type === "TrueFalse") {
        return {
          id: block.id,
          type: "true_false",
          prompt: String(config.question ?? block.title ?? ""),
          help,
          required,
          score,
          correctAnswer: answer?.correctAnswer === true
        };
      }

      // ShortAnswer y cualquier otro tipo no soportado caen a texto libre.
      const sampleAnswer = typeof answer?.correctAnswer === "string" ? answer.correctAnswer : undefined;
      const maxLength = typeof config.maxLength === "number" ? config.maxLength : undefined;
      return {
        id: block.id,
        type: "free_text",
        prompt: String(config.prompt ?? block.title ?? ""),
        help,
        required,
        score,
        sampleAnswer,
        maxLength
      };
    });

  return {
    schemaVersion: 1,
    code: exam.code,
    title: typeof metadata.title === "string" ? metadata.title : "",
    description: typeof metadata.description === "string" ? metadata.description : undefined,
    subject: typeof metadata.subject === "string" ? metadata.subject : exam.subject ?? undefined,
    level: typeof metadata.level === "string" ? metadata.level : exam.level ?? undefined,
    area: typeof metadata.area === "string" ? metadata.area : exam.area ?? undefined,
    questions
  };
}

function emptyOptions(count: number): ExamOption[] {
  return Array.from({ length: Math.max(0, count) }, () => ({ id: newId(), label: "", isCorrect: false }));
}

// Pregunta nueva en blanco segun tipo (para el boton "Agregar pregunta").
export function blankQuestion(type: Question["type"]): Question {
  const base = { id: newId(), prompt: "", required: true, score: 1 as number };
  switch (type) {
    case "single_choice":
    case "multiple_choice":
      return {
        ...base,
        type,
        options: [
          { id: newId(), label: "", isCorrect: false },
          { id: newId(), label: "", isCorrect: false }
        ]
      };
    case "true_false":
      return { ...base, type, correctAnswer: true };
    case "free_text":
      return { ...base, type };
  }
}
