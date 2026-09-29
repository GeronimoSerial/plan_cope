import {
  blockTypes,
  type BlockType,
  type DocumentBlock,
  type ExamVersion,
  type PublishBlockedReason,
  type ReplaceExamDocumentRequest,
  type ExamSummary
} from "../contracts";
import type { ExamDocument, Question, ExamOption, ScoringPolicy } from "./exam";

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
    blocks,
    // Known limitation: the API cannot clear a stored policy. ExamsController.ReplaceDocument
    // merges with `request.ScoringPolicy ?? version.ScoringPolicy`, so sending null here keeps the
    // previous policy server-side; removing every multiple-choice question does not unset it.
    scoringPolicy: document.scoringPolicy ?? null
  };
}

function questionToBlock(question: Question, orderIndex: number): DocumentBlock {
  const base = {
    orderIndex,
    title: (question.prompt ?? "").slice(0, 120),
    description: question.help ?? null,
    validation: { required: "required" in question ? question.required : false },
    scoreValue: "score" in question ? question.score : 0
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
    case "text_block":
      return {
        ...base,
        validation: { required: false },
        scoreValue: 0,
        blockType: "Text",
        config: { content: question.prompt, help: question.help ?? null }
      };
    case "image_block":
      return {
        ...base,
        validation: { required: false },
        scoreValue: 0,
        blockType: "Image",
        config: { assetId: question.assetId, caption: question.prompt ?? null, help: question.help ?? null }
      };
  }
}

// ---------- API -> Canonico (cargar en el builder) ----------
// Los metadatos de la version ganan cuando traen un valor util; los datos del ExamSummary
// (title/level/area/subject) actuan como fallback: POST /api/exams deja Metadata=null en la
// version inicial, asi que sin este fallback el builder abriria sin titulo.
function metadataText(value: unknown): string | undefined {
  return typeof value === "string" && value.trim().length > 0 ? value : undefined;
}

export function versionToDocument(
  version: ExamVersion,
  exam: Pick<ExamSummary, "code" | "title" | "subject" | "level" | "area">
): ExamDocument {
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

      if (type === "Text") {
        return {
          id: block.id,
          type: "text_block",
          prompt: String(config.content ?? block.title ?? ""),
          help
        };
      }

      if (type === "Image") {
        return {
          id: block.id,
          type: "image_block",
          prompt: typeof config.caption === "string" ? config.caption : undefined,
          assetId: String(config.assetId ?? ""),
          help
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
    title: metadataText(metadata.title) ?? exam.title,
    description: typeof metadata.description === "string" ? metadata.description : undefined,
    subject: metadataText(metadata.subject) ?? exam.subject ?? undefined,
    level: metadataText(metadata.level) ?? exam.level ?? undefined,
    area: metadataText(metadata.area) ?? exam.area ?? undefined,
    scoringPolicy: (version.scoringPolicy as ScoringPolicy | null) ?? null,
    questions
  };
}

function emptyOptions(count: number): ExamOption[] {
  return Array.from({ length: Math.max(0, count) }, () => ({ id: newId(), label: "", isCorrect: false }));
}

// Espeja EvaluatePublishReadiness de ExamsController: una version es publicable cuando tiene
// al menos un bloque y, si contiene un bloque MultipleChoice, tiene regla de puntaje.
// single_choice tambien se guarda como bloque MultipleChoice (config.multiple=false), por lo que
// el API le exige regla igual que a multiple_choice; mirror exacto de la validacion de publicacion.
export function documentNeedsScoringPolicy(document: ExamDocument): boolean {
  return document.questions.some(question => question.type === "single_choice" || question.type === "multiple_choice");
}

export type DocumentBlockedReason = "no_blocks" | "scoring_policy_required" | "unknown" | null;

export interface DocumentReadiness {
  canPublish: boolean;
  blockedReason: DocumentBlockedReason;
}

export function evaluateDocumentReadiness(document: ExamDocument): DocumentReadiness {
  if (document.questions.length === 0) {
    return { canPublish: false, blockedReason: "no_blocks" };
  }
  if (documentNeedsScoringPolicy(document) && !document.scoringPolicy) {
    return { canPublish: false, blockedReason: "scoring_policy_required" };
  }
  return { canPublish: true, blockedReason: null };
}

// Readiness reportada por el servidor (ExamVersionDto o la respuesta del PUT document).
export interface ServerReadiness {
  canPublish: boolean;
  blockedReason: PublishBlockedReason | null;
}

// Cuando hay cambios locales, manda la evaluacion en memoria. Sin cambios, manda el servidor:
// asi el boton Publicar no queda deshabilitado por un `canPublish` viejo tras guardar.
export function mergeDocumentReadiness(
  local: DocumentReadiness,
  server: ServerReadiness,
  dirty: boolean
): DocumentReadiness {
  if (dirty) {
    return local;
  }
  if (!server.canPublish) {
    if (server.blockedReason === "no_blocks" || server.blockedReason === "scoring_policy_required") {
      return { canPublish: false, blockedReason: server.blockedReason };
    }
    // Unknown/unmapped server reason (for example already_published, or a new reason this client
    // does not know yet): never trust the stale local evaluation. Keep the button disabled and let
    // the UI fall back to a generic message.
    return { canPublish: false, blockedReason: "unknown" };
  }
  return { canPublish: true, blockedReason: null };
}

// Duplica una pregunta con ids nuevos (la copia no comparte identidad con el original).
export function cloneQuestion(question: Question): Question {
  if (question.type === "single_choice" || question.type === "multiple_choice") {
    return {
      ...question,
      id: newId(),
      options: question.options.map(option => ({ ...option, id: newId() }))
    };
  }
  return { ...question, id: newId() };
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
    case "text_block":
      return { ...base, type, prompt: "" };
    case "image_block":
      return { ...base, type, assetId: "" };
  }
}
