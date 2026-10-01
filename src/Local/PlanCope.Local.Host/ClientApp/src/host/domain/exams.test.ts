import { describe, expect, it } from "vitest";
import { filterExams, toExamOption } from "./exams";
import type { LocalExam } from "../../shared/api-types";

const exam: LocalExam = {
  id: "version-1",
  remoteExamVersionId: "remote-version-1",
  examCode: "EXA-1",
  versionNumber: 1,
  checksum: "checksum",
  metadataJson: JSON.stringify({ title: "Evaluación", grade: ["primaria-6", "secundaria-1"] }),
  schemaVersion: 1,
  syncedAt: "2026-01-01T00:00:00Z"
};

describe("Local exam course metadata", () => {
  it("keeps every course from a multi-course publication", () => {
    const option = toExamOption(exam);

    expect(option.course).toEqual(["primaria-6", "secundaria-1"]);
    expect(filterExams([option], "primaria-6", "")).toEqual([option]);
    expect(filterExams([option], "secundaria-1", "")).toEqual([option]);
    expect(filterExams([option], "primaria-5", "")).toEqual([]);
  });
});
