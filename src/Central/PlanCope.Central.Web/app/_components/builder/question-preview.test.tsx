// @vitest-environment jsdom

import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import type { Question } from "../../_lib/schema/exam";
import { QuestionPreview } from "./question-preview";

afterEach(cleanup);

const base = {
  id: "q-1",
  prompt: "Elegí la respuesta",
  help: "Leé con atención",
  required: true,
  score: 1
};

describe("QuestionPreview", () => {
  it("renders single choice, help, required mark and image without disclosing the key", () => {
    const question = {
      ...base,
      type: "single_choice",
      imageAssetId: "asset 1",
      options: [
        { id: "a", label: "Primera opción", isCorrect: true },
        { id: "b", label: "Segunda opción", isCorrect: false }
      ]
    } as Question;

    const { container } = render(<QuestionPreview question={question} versionId="version/1" number={2} />);

    expect(screen.getByRole("heading", { level: 3 }).textContent).toContain("Elegí la respuesta");
    expect(screen.getByRole("heading", { level: 3 }).textContent).toContain("*");
    expect(screen.getByText("Leé con atención")).toBeTruthy();
    expect(screen.getByAltText("Elegí la respuesta").getAttribute("src")).toContain("version%2F1/assets/asset%201");
    expect(screen.getAllByRole("radio")).toHaveLength(2);
    expect(screen.getAllByRole("radio").every(option => (option as HTMLInputElement).checked === false)).toBe(true);
    expect(container.innerHTML).not.toMatch(/isCorrect|correctAnswer/);
  });

  it("renders multiple choice and true/false like the student's radio controls", () => {
    const multipleChoice = {
      ...base,
      type: "multiple_choice",
      options: [{ id: "a", label: "A", isCorrect: true }, { id: "b", label: "B", isCorrect: false }]
    } as Question;
    const trueFalse = { ...base, id: "q-2", type: "true_false", correctAnswer: true } as Question;

    const { container, rerender } = render(<QuestionPreview question={multipleChoice} versionId="v1" />);
    expect(screen.getAllByRole("radio")).toHaveLength(2);
    expect(screen.getAllByRole("radio").every(option => (option as HTMLInputElement).checked === false)).toBe(true);

    rerender(<QuestionPreview question={trueFalse} versionId="v1" />);
    expect(screen.getByText("Verdadero")).toBeTruthy();
    expect(screen.getByText("Falso")).toBeTruthy();
    expect(screen.getAllByRole("radio")).toHaveLength(2);
    expect(container.innerHTML).not.toMatch(/isCorrect|correctAnswer/);
  });

  it("shows useful placeholders for incomplete questions and reflects updated edits", () => {
    const incomplete = {
      ...base,
      prompt: " ",
      help: " ",
      required: false,
      type: "multiple_choice",
      options: []
    } as Question;

    const { rerender } = render(<QuestionPreview question={incomplete} versionId="v1" />);
    expect(screen.getByText("(sin enunciado)")).toBeTruthy();
    expect(screen.getByText("Sin opciones cargadas.")).toBeTruthy();

    rerender(<QuestionPreview question={{ ...incomplete, prompt: "Edición actual" }} versionId="v1" />);
    expect(screen.getByText("Edición actual")).toBeTruthy();
  });
});
