import { z } from "zod";
import type { GlossaryTerm } from "../glossary";

// ============================================================
// Canonical exam schema — the source of truth.
// The builder uses this model; the mappers translate it
// into Central API contracts and exportable JSON.
// ============================================================

export const questionTypes = [
  "single_choice",
  "multiple_choice",
  "true_false"
] as const;
export type QuestionType = (typeof questionTypes)[number];

export const questionTypeLabels: Record<QuestionType, string> = {
  single_choice: "Opción única",
  multiple_choice: "Opción múltiple",
  true_false: "Verdadero / Falso"
};

export const scoringPolicies = ["AllOrNothing", "ProportionalPenalised", "ProportionalPlain"] as const;
export type ScoringPolicy = (typeof scoringPolicies)[number];

export const scoringPolicyLabels: Record<ScoringPolicy, string> = {
  AllOrNothing: "Todo o nada",
  ProportionalPenalised: "Proporcional con penalización",
  ProportionalPlain: "Proporcional simple"
};

// Glossary term that explains each scoring policy.
export const scoringPolicyTerms: Record<ScoringPolicy, GlossaryTerm> = {
  AllOrNothing: "politica-all-or-nothing",
  ProportionalPenalised: "politica-proportional-penalised",
  ProportionalPlain: "politica-proportional-plain"
};

/** True cuando el string del API corresponde a una política de puntaje conocida. */
export function isScoringPolicy(value: string | null | undefined): value is ScoringPolicy {
  return value === "AllOrNothing" || value === "ProportionalPenalised" || value === "ProportionalPlain";
}

export const scoringPolicyExplanations: Record<ScoringPolicy, string> = {
  AllOrNothing: "Solo suma si marca exactamente las correctas.",
  ProportionalPenalised: "Suma por cada correcta y resta por cada incorrecta, sin bajar de 0.",
  ProportionalPlain: "Suma por cada correcta marcada; no penaliza las incorrectas."
};

// Caveat shown under the option only while it is selected.
export const scoringPolicyWarnings: Partial<Record<ScoringPolicy, string>> = {
  ProportionalPlain: "Marcar todas las opciones da el puntaje completo."
};

const optionSchema = z.object({
  id: z.string().min(1),
  label: z.string().trim().min(1, "La opción no puede estar vacía."),
  isCorrect: z.boolean()
});

const baseQuestion = z.object({
  id: z.string().min(1),
  prompt: z.string().trim().min(1, "El enunciado es requerido."),
  help: z.string().trim().optional(),
  required: z.boolean(),
  score: z.number().min(0, "El puntaje no puede ser negativo.")
});

const choiceQuestionBase = baseQuestion.extend({
  options: z.array(optionSchema).min(2, "Se requieren al menos 2 opciones.")
});

const singleChoiceQuestion = choiceQuestionBase.extend({ type: z.literal("single_choice") });
const multipleChoiceQuestion = choiceQuestionBase.extend({
  type: z.literal("multiple_choice"),
  scoringPolicy: z.enum(scoringPolicies).default("AllOrNothing")
});

const trueFalseQuestion = baseQuestion.extend({
  type: z.literal("true_false"),
  correctAnswer: z.boolean()
});

export const questionSchema = z.discriminatedUnion("type", [
  singleChoiceQuestion,
  multipleChoiceQuestion,
  trueFalseQuestion
]).superRefine((question, context) => {
  if (question.type === "single_choice" || question.type === "multiple_choice") {
    if (!question.options.some(option => option.isCorrect)) {
      context.addIssue({ code: "custom", message: "Marca al menos una opción correcta.", path: ["options"] });
    }
    if (question.type === "single_choice" && question.options.filter(option => option.isCorrect).length !== 1) {
      context.addIssue({ code: "custom", message: "La opción única admite una sola respuesta correcta.", path: ["options"] });
    }
  }
});

export const examDocumentSchema = z.object({
  schemaVersion: z.literal(1),
  code: z.string().trim().min(1, "El código es requerido."),
  title: z.string().trim().min(1, "El título es requerido."),
  description: z.string().trim().optional(),
  subject: z.string().trim().optional(),
  level: z.string().trim().optional(),
  area: z.string().trim().optional(),
  questions: z.array(questionSchema).min(1, "Agregá al menos una pregunta.")
});

export type ExamOption = z.infer<typeof optionSchema>;
export type Question = z.input<typeof questionSchema>;
export type ExamDocument = z.input<typeof examDocumentSchema>;

// ---- Create exam (initial metadata) ----
export const createExamSchema = z.object({
  code: z.string().trim().min(1, "El código es requerido.").max(64),
  title: z.string().trim().min(1, "El título es requerido.").max(256),
  subject: z.string().trim().optional(),
  level: z.string().trim().optional(),
  area: z.string().trim().optional(),
  description: z.string().trim().optional()
});

export type CreateExamValues = z.infer<typeof createExamSchema>;

// ---- Publication ----
export const publishSchema = z.object({
  subject: z.string().trim().optional(),
  grade: z.string().trim().min(1, "El curso/grado es requerido."),
  division: z.string().trim().optional()
});

export type PublishValues = z.infer<typeof publishSchema>;
