import { z } from "zod";

// ============================================================
// Schema canonico del examen — FUENTE DE VERDAD.
// El builder trabaja con este modelo; los mappers lo traducen
// a los contratos del Central API y al JSON exportable.
// ============================================================

export const questionTypes = ["single_choice", "multiple_choice", "true_false", "free_text"] as const;
export type QuestionType = (typeof questionTypes)[number];

export const questionTypeLabels: Record<QuestionType, string> = {
  single_choice: "Opción única",
  multiple_choice: "Opción múltiple",
  true_false: "Verdadero / Falso",
  free_text: "Texto libre"
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

const choiceQuestion = baseQuestion
  .extend({
    type: z.enum(["single_choice", "multiple_choice"]),
    options: z.array(optionSchema).min(2, "Se requieren al menos 2 opciones.")
  })
  .refine(question => question.options.some(option => option.isCorrect), {
    message: "Marca al menos una opción correcta.",
    path: ["options"]
  })
  .refine(
    question => question.type !== "single_choice" || question.options.filter(option => option.isCorrect).length === 1,
    {
      message: "La opción única admite una sola respuesta correcta.",
      path: ["options"]
    }
  );

const trueFalseQuestion = baseQuestion.extend({
  type: z.literal("true_false"),
  correctAnswer: z.boolean()
});

const freeTextQuestion = baseQuestion.extend({
  type: z.literal("free_text"),
  sampleAnswer: z.string().trim().optional(),
  maxLength: z.number().int().positive().optional()
});

export const questionSchema = z.discriminatedUnion("type", [choiceQuestion, trueFalseQuestion, freeTextQuestion]);

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
export type Question = z.infer<typeof questionSchema>;
export type ExamDocument = z.infer<typeof examDocumentSchema>;

// ---- Alta de examen (metadata inicial) ----
export const createExamSchema = z.object({
  code: z.string().trim().min(1, "El código es requerido.").max(64),
  title: z.string().trim().min(1, "El título es requerido.").max(256),
  subject: z.string().trim().optional(),
  level: z.string().trim().optional(),
  area: z.string().trim().optional(),
  description: z.string().trim().optional()
});

export type CreateExamValues = z.infer<typeof createExamSchema>;

// ---- Publicación ----
export const publishSchema = z.object({
  subject: z.string().trim().optional(),
  grade: z.string().trim().min(1, "El curso/grado es requerido."),
  division: z.string().trim().optional()
});

export type PublishValues = z.infer<typeof publishSchema>;
