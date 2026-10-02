// @vitest-environment jsdom

import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { Question } from "../../_lib/schema/exam";
import { QuestionCard } from "./question-card";

vi.mock("@dnd-kit/sortable", () => ({
  useSortable: () => ({
    attributes: {},
    listeners: {},
    setNodeRef: vi.fn(),
    transform: null,
    transition: undefined,
    isDragging: false
  })
}));
vi.mock("@dnd-kit/utilities", () => ({ CSS: { Transform: { toString: () => undefined } } }));
vi.mock("./question-editor", () => ({ QuestionEditor: () => <div data-testid="question-editor" /> }));

afterEach(cleanup);

describe("QuestionCard preview action", () => {
  it("opens the current question in an accessible dialog and closes with Escape", async () => {
    const question = {
      id: "q1",
      type: "single_choice",
      prompt: "Texto editado",
      help: "Ayuda vigente",
      required: true,
      score: 1,
      options: [{ id: "a", label: "Opción A", isCorrect: true }, { id: "b", label: "Opción B", isCorrect: false }]
    } as Question;
    const onUpdate = vi.fn();

    render(
      <QuestionCard
        question={question}
        versionId="v1"
        index={0}
        questionCount={1}
        errors={{}}
        onUpdate={onUpdate}
        onMove={vi.fn()}
        onRemove={vi.fn()}
        onDuplicate={vi.fn()}
      />
    );

    const trigger = screen.getByRole("button", { name: "Vista previa de la pregunta 1" });
    fireEvent.click(trigger);

    const dialog = screen.getByRole("dialog");
    expect(dialog.textContent).toContain("Texto editado");
    expect(dialog.textContent).toContain("Ayuda vigente");
    expect(dialog.querySelectorAll("input:disabled")).toHaveLength(2);
    expect(document.activeElement).not.toBe(trigger);

    fireEvent.keyDown(document, { key: "Escape" });
    expect(screen.queryByRole("dialog")).toBeNull();
    await waitFor(() => expect(document.activeElement).toBe(trigger));
  });
});
