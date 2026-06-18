import { z } from "zod";

const nonEmptyString = z.string().trim().min(1);

export const examAssetSchema = z.object({
  id: nonEmptyString,
  fileName: nonEmptyString,
  mimeType: z.string().trim().refine(value => value.startsWith("image/"), "El MIME type debe ser de imagen."),
  contentBase64: nonEmptyString.refine(isBase64, "El contenido del asset debe estar en base64 valido.")
});

const validationSchema = z
  .object({
    required: z.boolean().optional()
  })
  .optional();

const answerKeySchema = z
  .object({
    correctAnswer: z.union([z.string(), z.boolean()]),
    scoreValue: z.number().min(0)
  })
  .optional();

const textBlockSchema = z.object({
  id: nonEmptyString,
  type: z.literal("text"),
  config: z.object({
    content: nonEmptyString
  })
});

const imageBlockSchema = z.object({
  id: nonEmptyString,
  type: z.literal("image"),
  config: z.object({
    assetId: nonEmptyString,
    alt: z.string().optional(),
    caption: z.string().optional()
  })
});

const multipleChoiceBlockSchema = z
  .object({
    id: nonEmptyString,
    type: z.literal("multiple_choice"),
    config: z.object({
      question: nonEmptyString,
      options: z
        .array(
          z.object({
            value: nonEmptyString,
            label: nonEmptyString
          })
        )
        .min(2, "Una pregunta de opcion multiple necesita al menos dos opciones.")
    }),
    validation: validationSchema,
    answerKey: answerKeySchema
  })
  .superRefine((block, ctx) => {
    const values = block.config.options.map(option => option.value);
    const duplicates = values.filter((value, index) => values.indexOf(value) !== index);
    if (duplicates.length > 0) {
      ctx.addIssue({
        code: "custom",
        path: ["config", "options"],
        message: "Los valores de opcion multiple no pueden repetirse."
      });
    }

    if (block.answerKey && !values.includes(String(block.answerKey.correctAnswer))) {
      ctx.addIssue({
        code: "custom",
        path: ["answerKey", "correctAnswer"],
        message: "La respuesta correcta debe coincidir con una opcion disponible."
      });
    }
  });

const trueFalseBlockSchema = z
  .object({
    id: nonEmptyString,
    type: z.literal("true_false"),
    config: z.object({
      question: nonEmptyString
    }),
    validation: validationSchema,
    answerKey: answerKeySchema
  })
  .superRefine((block, ctx) => {
    if (block.answerKey && typeof block.answerKey.correctAnswer !== "boolean") {
      ctx.addIssue({
        code: "custom",
        path: ["answerKey", "correctAnswer"],
        message: "La respuesta correcta de verdadero/falso debe ser booleana."
      });
    }
  });

const shortAnswerBlockSchema = z
  .object({
    id: nonEmptyString,
    type: z.literal("short_answer"),
    config: z.object({
      prompt: nonEmptyString
    }),
    validation: validationSchema,
    answerKey: answerKeySchema
  })
  .superRefine((block, ctx) => {
    if (block.answerKey && typeof block.answerKey.correctAnswer !== "string") {
      ctx.addIssue({
        code: "custom",
        path: ["answerKey", "correctAnswer"],
        message: "La respuesta esperada de respuesta corta debe ser texto."
      });
    }
  });

export const examBlockSchema = z.discriminatedUnion("type", [
  textBlockSchema,
  imageBlockSchema,
  multipleChoiceBlockSchema,
  trueFalseBlockSchema,
  shortAnswerBlockSchema
]);

export const localExamJsonSchema = z
  .object({
    id: z.string().trim().optional(),
    examCode: nonEmptyString,
    title: nonEmptyString,
    grade: z.string().trim().optional(),
    division: z.string().trim().optional(),
    subject: z.string().trim().optional(),
    versionNumber: z.number().int().positive().optional(),
    assets: z.array(examAssetSchema).default([]),
    blocks: z.array(examBlockSchema).min(1, "El examen debe tener al menos un bloque.").default([])
  })
  .superRefine((exam, ctx) => {
    const assetIds = new Set(exam.assets.map(asset => asset.id));
    const duplicatedAssetIds = exam.assets.map(asset => asset.id).filter((id, index, ids) => ids.indexOf(id) !== index);
    const blockIds = exam.blocks.map(block => block.id);
    const duplicatedBlockIds = blockIds.filter((id, index) => blockIds.indexOf(id) !== index);

    if (duplicatedAssetIds.length > 0) {
      ctx.addIssue({
        code: "custom",
        path: ["assets"],
        message: "Los IDs de asset deben ser unicos."
      });
    }

    if (duplicatedBlockIds.length > 0) {
      ctx.addIssue({
        code: "custom",
        path: ["blocks"],
        message: "Los IDs de bloque deben ser unicos."
      });
    }

    exam.blocks.forEach((block, index) => {
      if (block.type === "image" && !assetIds.has(block.config.assetId)) {
        ctx.addIssue({
          code: "custom",
          path: ["blocks", index, "config", "assetId"],
          message: `El bloque de imagen apunta a un asset inexistente: ${block.config.assetId}.`
        });
      }
    });
  });

export type LocalExamJsonInput = z.input<typeof localExamJsonSchema>;

function isBase64(value: string): boolean {
  try {
    return btoa(atob(value)) === value;
  } catch {
    return false;
  }
}
