import type { LocalExamBlock } from "../../shared/api-types";
import { hasAnswer, isAnswerBlock, parseValidation } from "../examBlocks";

export type AnswerMap = Record<string, string>;

export function getInitialSessionCode(): string {
  return decodeURIComponent(window.location.pathname.split("/").filter(Boolean)[1] ?? "");
}

export function questionNumberFor(blocks: LocalExamBlock[], index: number): number | null {
  if (!isAnswerBlock(blocks[index])) {
    return null;
  }

  return blocks.slice(0, index + 1).filter(isAnswerBlock).length;
}

export function collectAnswers(blocks: LocalExamBlock[], answers: AnswerMap) {
  return blocks.filter(isAnswerBlock).map(block => ({
    blockId: block.id,
    answer: answers[block.id] ?? null
  }));
}

export function findMissingRequiredAnswers(blocks: LocalExamBlock[], answers: AnswerMap): Set<string> {
  return new Set(
    blocks
      .filter(isAnswerBlock)
      .filter(block => parseValidation(block).required === true)
      .filter(block => !hasAnswer(answers[block.id]))
      .map(block => block.id)
  );
}
