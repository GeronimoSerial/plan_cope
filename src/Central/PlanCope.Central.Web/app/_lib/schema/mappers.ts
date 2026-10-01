import {
  blockTypes,
  type BlockType,
  type DocumentBlock,
  type ExamVersion,
  type PublishBlockedReason,
  type ReplaceExamDocumentRequest,
  type ExamSummary
} from "../contracts";
import { isScoringPolicy, type ExamDocument, type Question, type ExamOption } from "./exam";

// ============================================================
// Anti-corruption layer: translates between the canonical schema (the source
// of truth for the builder) and Central API contracts.
// ============================================================

export function newId(): string {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID();
  }
  return `id-${Math.random().toString(36).slice(2)}-${Date.now()}`;
}

// BlockType can arrive as a string (with JsonStringEnumConverter)
// or as its persisted numeric enum value for compatibility.
export function normalizeBlockType(value: BlockType | number | string): BlockType {
  if (typeof value === "number") {
    // The persisted .NET enum values are kept stable for existing choice blocks.
    if (value === 2) return "MultipleChoice";
    if (value === 3) return "TrueFalse";
  } else if ((blockTypes as readonly string[]).includes(value)) {
    return value as BlockType;
  }
  throw new Error(`Unsupported exam block type: ${String(value)}`);
}

// ---------- Canonical -> API (save) ----------
export function documentToReplaceRequest(document: ExamDocument): ReplaceExamDocumentRequest {
  const blocks: DocumentBlock[] = document.questions.map((question, index) =>
    questionToBlock(question, index)
  );

  return {
    metadata: {
      title: document.title,
      description: document.description ?? null,
      subject: document.subject ?? null,
      courses: document.courses,
      area: document.area ?? null
    },
    blocks
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
          ...(question.imageAssetId ? { imageAssetId: question.imageAssetId } : {}),
          multiple: question.type === "multiple_choice",
          ...(question.type === "multiple_choice" ? { scoringPolicy: question.scoringPolicy ?? "AllOrNothing" } : {}),
          options: question.options.map(option => ({ value: option.id, label: option.label }))
        },
        correctAnswer: question.options.filter(option => option.isCorrect).map(option => option.id)
      };
    case "true_false":
      return {
        ...base,
        blockType: "TrueFalse",
        config: { question: question.prompt, help: question.help ?? null, ...(question.imageAssetId ? { imageAssetId: question.imageAssetId } : {}) },
        correctAnswer: question.correctAnswer
      };
  }
}

// ---------- API -> canonical (load in the builder) ----------
// Version metadata takes precedence when it has a useful value; ExamSummary data
// (title/courses/area/subject) is the fallback because POST /api/exams leaves Metadata=null in the
// initial version, so without this fallback the builder would have no title.
function metadataText(value: unknown): string | undefined {
  return typeof value === "string" && value.trim().length > 0 ? value : undefined;
}

export function versionToDocument(
  version: ExamVersion,
  exam: Pick<ExamSummary, "code" | "title" | "subject" | "courses" | "area">
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
      const imageAssetId = typeof config.imageAssetId === "string" && config.imageAssetId.length > 0 ? config.imageAssetId : undefined;

      if (type === "MultipleChoice") {
        const multiple = config.multiple === true;
        const rawScoringPolicy = config.scoringPolicy;
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
          ...(multiple ? { scoringPolicy: isScoringPolicy(rawScoringPolicy) ? rawScoringPolicy : "AllOrNothing" } : {}),
          prompt: String(config.question ?? block.title ?? ""),
          imageAssetId,
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
          imageAssetId,
          help,
          required,
          score,
          correctAnswer: answer?.correctAnswer === true
        };
      }

      throw new Error(`Unsupported exam block type: ${String(block.blockType)}`);
    });

  return {
    schemaVersion: 1,
    code: exam.code,
    title: metadataText(metadata.title) ?? exam.title,
    description: typeof metadata.description === "string" ? metadata.description : undefined,
    subject: metadataText(metadata.subject) ?? exam.subject ?? undefined,
    courses: exam.courses,
    area: exam.area ?? undefined,
    questions
  };
}

function emptyOptions(count: number): ExamOption[] {
  return Array.from({ length: Math.max(0, count) }, () => ({ id: newId(), label: "", isCorrect: false }));
}

export type DocumentBlockedReason = "no_blocks" | "unknown" | null;

export interface DocumentReadiness {
  canPublish: boolean;
  blockedReason: DocumentBlockedReason;
}

export function evaluateDocumentReadiness(document: ExamDocument): DocumentReadiness {
  if (document.questions.length === 0) {
    return { canPublish: false, blockedReason: "no_blocks" };
  }
  return { canPublish: true, blockedReason: null };
}

// Readiness reported by the server (ExamVersionDto or the PUT document response).
export interface ServerReadiness {
  canPublish: boolean;
  blockedReason: PublishBlockedReason | null;
}

// With local changes, use the in-memory evaluation. Otherwise, use the server result:
// this prevents a stale `canPublish` value from disabling Publish after a save.
export function mergeDocumentReadiness(
  local: DocumentReadiness,
  server: ServerReadiness,
  dirty: boolean
): DocumentReadiness {
  if (dirty) {
    return local;
  }
  if (!server.canPublish) {
    if (server.blockedReason === "no_blocks") {
      return { canPublish: false, blockedReason: server.blockedReason };
    }
    // Unknown/unmapped server reason (for example already_published, or a new reason this client
    // does not know yet): never trust the stale local evaluation. Keep the button disabled and let
    // the UI fall back to a generic message.
    return { canPublish: false, blockedReason: "unknown" };
  }
  return { canPublish: true, blockedReason: null };
}

// Duplicate a question with new IDs so it does not share identity with the original.
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

// Create a blank question of the requested type for the Add question button.
export function blankQuestion(type: Question["type"]): Question {
  const base = { id: newId(), prompt: "", required: true, score: 1 as number };
  switch (type) {
    case "single_choice":
    case "multiple_choice":
      return {
        ...base,
        type,
        ...(type === "multiple_choice" ? { scoringPolicy: "AllOrNothing" as const } : {}),
        options: [
          { id: newId(), label: "", isCorrect: false },
          { id: newId(), label: "", isCorrect: false }
        ]
      };
    case "true_false":
      return { ...base, type, correctAnswer: true };
  }
}
