import { afterEach, describe, expect, it } from "vitest";
import type { LocalExamBlock } from "../../shared/api-types";
import {
  collectAnswers,
  findMissingRequiredAnswers,
  getInitialSessionCode,
  questionNumberFor
} from "./examAnswers";

let nextBlockId = 0;

function makeBlock(overrides: Partial<LocalExamBlock> = {}): LocalExamBlock {
  const id = `block-${nextBlockId++}`;
  return {
    id,
    localExamVersionId: "local-version-1",
    remoteBlockId: `remote-${id}`,
    orderIndex: 0,
    blockType: "Text",
    configJson: "{}",
    validationJson: undefined,
    ...overrides
  };
}

function setPathname(pathname: string) {
  Object.defineProperty(globalThis, "window", {
    value: { location: { pathname } },
    configurable: true,
    writable: true
  });
}

afterEach(() => {
  Reflect.deleteProperty(globalThis, "window");
});

describe("questionNumberFor", () => {
  const blocks = [
    makeBlock({ id: "text-1", blockType: "Text" }),
    makeBlock({ id: "mc-1", blockType: "MultipleChoice" }),
    makeBlock({ id: "image-1", blockType: "Image" }),
    makeBlock({ id: "tf-1", blockType: "TrueFalse" })
  ];

  it("returns null for non-answer blocks and 1-based ordinal position for answer blocks", () => {
    expect(questionNumberFor(blocks, 0)).toBeNull();
    expect(questionNumberFor(blocks, 1)).toBe(1);
    expect(questionNumberFor(blocks, 2)).toBeNull();
    expect(questionNumberFor(blocks, 3)).toBe(2);
  });
});

describe("collectAnswers", () => {
  const blocks = [
    makeBlock({ id: "text-1", blockType: "Text" }),
    makeBlock({ id: "mc-1", blockType: "MultipleChoice" }),
    makeBlock({ id: "image-1", blockType: "Image" }),
    makeBlock({ id: "tf-1", blockType: "TrueFalse" })
  ];

  it("returns one entry per answer block with the matching answer", () => {
    expect(collectAnswers(blocks, { "mc-1": "A", "tf-1": "X" })).toEqual([
      { blockId: "mc-1", answer: "A" },
      { blockId: "tf-1", answer: "X" }
    ]);
  });

  it("uses null when the answers map has no entry for an answer block", () => {
    expect(collectAnswers(blocks, { "mc-1": "A" })).toEqual([
      { blockId: "mc-1", answer: "A" },
      { blockId: "tf-1", answer: null }
    ]);
  });
});

describe("findMissingRequiredAnswers", () => {
  const blocks = [
    makeBlock({ id: "text-1", blockType: "Text", validationJson: '{"required":true}' }),
    makeBlock({ id: "image-1", blockType: "Image", validationJson: '{"required":true}' }),
    makeBlock({ id: "mc-1", blockType: "MultipleChoice", validationJson: '{"required":true}' }),
    makeBlock({ id: "tf-1", blockType: "TrueFalse", validationJson: '{"required":true}' }),
    makeBlock({ id: "sa-1", blockType: "Essay", validationJson: '{"required":false}' }),
    makeBlock({ id: "mc-2", blockType: "MultipleChoice", validationJson: '{"required":true}' })
  ];

  it("returns exactly the required, unanswered answer block ids", () => {
    const missing = findMissingRequiredAnswers(blocks, { "mc-1": "A", "mc-2": "   " });
    expect(missing).toEqual(new Set(["tf-1", "mc-2"]));
  });

  it("excludes a required-and-answered block", () => {
    const missing = findMissingRequiredAnswers(blocks, { "mc-1": "A", "tf-1": "B" });
    expect(missing.has("mc-1")).toBe(false);
  });

  it("never includes a non-required unanswered block", () => {
    const missing = findMissingRequiredAnswers(blocks, {});
    expect(missing.has("sa-1")).toBe(false);
    expect(missing.has("mc-1")).toBe(true);
  });

  it("ignores text/image blocks even when marked required", () => {
    const missing = findMissingRequiredAnswers(blocks, {});
    expect(missing.has("text-1")).toBe(false);
    expect(missing.has("image-1")).toBe(false);
  });
});

describe("getInitialSessionCode", () => {
  it("extracts the access code from an /examen/<accessCode> pathname", () => {
    setPathname("/examen/ABC-123");
    expect(getInitialSessionCode()).toBe("ABC-123");
  });

  it("returns an empty string for the root path", () => {
    setPathname("/");
    expect(getInitialSessionCode()).toBe("");
  });
});