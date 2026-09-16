import { describe, expect, it } from "vitest";
import type { LocalExamBlock } from "../shared/api-types";
import { getBlockKind, hasAnswer, isAnswerBlock, parseConfig, parseValidation } from "./examBlocks";

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

describe("getBlockKind", () => {
  it.each([
    [0, "text"],
    ["Text", "text"],
    [1, "image"],
    ["Image", "image"],
    [2, "multiple_choice"],
    ["MultipleChoice", "multiple_choice"],
    [3, "true_false"],
    ["TrueFalse", "true_false"]
  ] as const)("maps blockType %s to %s", (blockType, kind) => {
    expect(getBlockKind(makeBlock({ blockType }))).toBe(kind);
  });

  it("falls through to short_answer for unrecognized block types", () => {
    expect(getBlockKind(makeBlock({ blockType: 99 }))).toBe("short_answer");
    expect(getBlockKind(makeBlock({ blockType: "Essay" }))).toBe("short_answer");
  });
});

describe("parseConfig", () => {
  it("parses valid JSON", () => {
    expect(parseConfig<{ label: string }>(makeBlock({ configJson: '{"label":"A"}' }))).toEqual({
      label: "A"
    });
  });

  it("returns an empty object instead of throwing on malformed JSON", () => {
    expect(parseConfig(makeBlock({ configJson: "not json {" }))).toEqual({});
    expect(parseConfig(makeBlock({ configJson: "" }))).toEqual({});
  });
});

describe("parseValidation", () => {
  it("returns an empty object for undefined or null validationJson", () => {
    expect(parseValidation(makeBlock({ validationJson: undefined }))).toEqual({});
    expect(parseValidation(makeBlock({ validationJson: null }))).toEqual({});
  });

  it("parses valid validation JSON", () => {
    expect(parseValidation(makeBlock({ validationJson: '{"required":true}' }))).toEqual({
      required: true
    });
  });

  it("returns an empty object instead of throwing on malformed JSON", () => {
    expect(parseValidation(makeBlock({ validationJson: "{oops" }))).toEqual({});
  });
});

describe("isAnswerBlock", () => {
  it.each(["MultipleChoice", "TrueFalse", "Essay"])(
    "returns true for answer kinds (%s)",
    blockType => {
      expect(isAnswerBlock(makeBlock({ blockType }))).toBe(true);
    }
  );

  it.each(["Text", "Image"])("returns false for non-answer kinds (%s)", blockType => {
    expect(isAnswerBlock(makeBlock({ blockType }))).toBe(false);
  });
});

describe("hasAnswer", () => {
  it.each([null, undefined, "", "   ", "\n\t "])("returns false for blank value %j", value => {
    expect(hasAnswer(value)).toBe(false);
  });

  it("returns true for any non-blank string", () => {
    expect(hasAnswer("A")).toBe(true);
    expect(hasAnswer("  respuesta  ")).toBe(true);
  });
});