import { localExamJsonSchema } from "./examSchema";
import type {
  ExamAsset,
  ExamBlock,
  ExamBlockType,
  ExamBuilderFormValues,
  LocalExamJson
} from "./examTypes";

export function createStableId(prefix: string): string {
  const randomPart = Math.random().toString(36).slice(2, 9);
  return `${prefix}-${Date.now().toString(36)}-${randomPart}`;
}

export function createEmptyExam(): ExamBuilderFormValues {
  return {
    id: createStableId("exam"),
    examCode: "",
    title: "",
    grade: "",
    division: "",
    subject: "",
    versionNumber: 1,
    assets: [],
    blocks: []
  };
}

export function createDefaultBlock(type: ExamBlockType): ExamBlock {
  const id = createStableId(type.replace("_", "-"));

  if (type === "text") {
    return { id, type, config: { content: "" } };
  }

  if (type === "image") {
    return { id, type, config: { assetId: "", alt: "", caption: "" } };
  }

  if (type === "multiple_choice") {
    return {
      id,
      type,
      config: {
        question: "",
        options: [
          { value: "opcion-1", label: "Opcion 1" },
          { value: "opcion-2", label: "Opcion 2" }
        ]
      },
      validation: { required: true }
    };
  }

  if (type === "true_false") {
    return { id, type, config: { question: "" }, validation: { required: true } };
  }

  return { id, type, config: { prompt: "" }, validation: { required: true } };
}

export function normalizeExam(input: unknown): ExamBuilderFormValues {
  const parsed = localExamJsonSchema.parse(input);

  return {
    id: parsed.id ?? createStableId("exam"),
    examCode: parsed.examCode,
    title: parsed.title,
    grade: parsed.grade ?? "",
    division: parsed.division ?? "",
    subject: parsed.subject ?? "",
    versionNumber: parsed.versionNumber ?? 1,
    assets: parsed.assets ?? [],
    blocks: parsed.blocks ?? []
  };
}

export function buildExamJson(values: ExamBuilderFormValues): LocalExamJson {
  const json: LocalExamJson = {
    id: values.id.trim() || undefined,
    examCode: values.examCode.trim(),
    title: values.title.trim(),
    grade: values.grade.trim() || undefined,
    division: values.division.trim() || undefined,
    subject: values.subject.trim() || undefined,
    versionNumber: Number(values.versionNumber) || 1,
    assets: values.assets,
    blocks: values.blocks
  };

  return localExamJsonSchema.parse(json);
}

export function validateAssetReferences(exam: LocalExamJson): string[] {
  const assetIds = new Set(exam.assets.map(asset => asset.id));
  return exam.blocks.flatMap(block => {
    if (block.type !== "image" || assetIds.has(block.config.assetId)) {
      return [];
    }

    return [`El bloque ${block.id} usa el asset inexistente ${block.config.assetId}.`];
  });
}

export function serializeExamJson(exam: LocalExamJson): string {
  return `${JSON.stringify(exam, null, 2)}\n`;
}

export async function imageFileToAsset(file: File): Promise<ExamAsset> {
  if (!file.type.startsWith("image/")) {
    throw new Error("Solo se admiten archivos de imagen.");
  }

  const dataUrl = await readFileAsDataUrl(file);
  const [, contentBase64 = ""] = dataUrl.split(",");

  return {
    id: createStableId("asset"),
    fileName: file.name,
    mimeType: file.type,
    contentBase64
  };
}

function readFileAsDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(new Error("No se pudo leer el archivo."));
    reader.readAsDataURL(file);
  });
}
