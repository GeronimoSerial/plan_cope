// @vitest-environment jsdom

import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import type { ExamDocument } from "../../_lib/schema/exam";
import { ExamPreview } from "./exam-preview";

afterEach(cleanup);

describe("ExamPreview", () => {
  it("uses the shared question renderer in document order with current question content", () => {
    const document = {
      schemaVersion: 1,
      code: "EX-1",
      title: "Evaluación de ciencias",
      description: "Primer trimestre",
      subject: "Ciencias",
      courses: [],
      questions: [
        { id: "q1", type: "single_choice", prompt: "Primera", help: "Ayuda uno", required: true, score: 1, options: [{ id: "a", label: "A", isCorrect: true }, { id: "b", label: "B", isCorrect: false }] },
        { id: "q2", type: "true_false", prompt: "Segunda", required: false, score: 1, correctAnswer: false }
      ]
    } as ExamDocument;

    render(<ExamPreview document={document} versionId="version-current" />);

    expect(screen.getByRole("heading", { name: "Evaluación de ciencias" })).toBeTruthy();
    const questions = screen.getAllByRole("heading", { level: 3 });
    expect(questions[0].textContent).toContain("1");
    expect(questions[0].textContent).toContain("Primera");
    expect(questions[0].textContent).toContain("*");
    expect(questions[1].textContent).toContain("2");
    expect(questions[1].textContent).toContain("Segunda");
    expect(screen.getByText("Ayuda uno")).toBeTruthy();
    expect(screen.getAllByRole("radio")).toHaveLength(4);
  });
});
