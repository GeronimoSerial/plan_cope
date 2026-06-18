import type { LocalExamBlock } from "../types";

export type BlockKind = "text" | "image" | "multiple_choice" | "true_false" | "short_answer";

export type BlockValidation = {
  required?: boolean;
};

export function getBlockKind(block: LocalExamBlock): BlockKind {
  if (block.blockType === 0 || block.blockType === "Text") return "text";
  if (block.blockType === 1 || block.blockType === "Image") return "image";
  if (block.blockType === 2 || block.blockType === "MultipleChoice") return "multiple_choice";
  if (block.blockType === 3 || block.blockType === "TrueFalse") return "true_false";
  return "short_answer";
}

export function parseConfig<T extends object>(block: LocalExamBlock): T {
  try {
    return JSON.parse(block.configJson) as T;
  } catch {
    return {} as T;
  }
}

export function parseValidation(block: LocalExamBlock): BlockValidation {
  if (!block.validationJson) {
    return {};
  }

  try {
    return JSON.parse(block.validationJson) as BlockValidation;
  } catch {
    return {};
  }
}

export function isAnswerBlock(block: LocalExamBlock): boolean {
  return !["text", "image"].includes(getBlockKind(block));
}

export function hasAnswer(value: string | null | undefined): boolean {
  return value !== null && value !== undefined && value.trim().length > 0;
}
