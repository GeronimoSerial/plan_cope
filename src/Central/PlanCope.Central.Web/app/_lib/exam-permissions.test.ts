import { describe, expect, it } from "vitest";
import { canEditExams, examWriteErrorMessage, EXAM_EDIT_PERMISSION_MESSAGE } from "./exam-permissions";

describe("exam permissions", () => {
  it.each([["Admin", true], ["ExamAuthor", true], ["Grader", false], ["Operator", false], [null, false]] as const)("checks role %s", (role, expected) => {
    expect(canEditExams(role)).toBe(expected);
  });
  it("maps forbidden exam writes to the permission message", () => {
    expect(examWriteErrorMessage(403, "Forbidden")).toBe(EXAM_EDIT_PERMISSION_MESSAGE);
    expect(examWriteErrorMessage(400, "Invalid")).toBe("Invalid");
  });
});
