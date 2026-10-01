import type { LocalExamBlock } from "../shared/api-types";

export type BlockKind = "multiple_choice" | "true_false";

export type BlockValidation = {
  required?: boolean;
};

export function getBlockKind(block: LocalExamBlock): BlockKind {
  if (block.blockType === 2 || block.blockType === "MultipleChoice") return "multiple_choice";
  if (block.blockType === 3 || block.blockType === "TrueFalse") return "true_false";
  throw new Error(`Unsupported exam block type: ${String(block.blockType)}`);
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
  getBlockKind(block);
  return true;
}

export function hasAnswer(value: string | null | undefined): boolean {
  return value !== null && value !== undefined && value.trim().length > 0;
}
