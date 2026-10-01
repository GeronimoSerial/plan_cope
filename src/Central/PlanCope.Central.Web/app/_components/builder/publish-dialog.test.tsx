// @vitest-environment jsdom

import type { ButtonHTMLAttributes, HTMLAttributes, InputHTMLAttributes, ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import type { ExamDocument } from "../../_lib/schema/exam";
import { PublishDialog } from "./publish-dialog";

const mocks = vi.hoisted(() => ({
  callCentral: vi.fn(),
  push: vi.fn()
}));

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: mocks.push }) }));
vi.mock("../../_lib/api/client", () => ({ callCentral: mocks.callCentral }));
vi.mock("@/components/ui/button", () => ({
  Button: ({ children, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: string }) => (
    <button {...props}>{children}</button>
  )
}));
vi.mock("@/components/ui/dialog", () => ({
  Dialog: ({ open, children }: { open: boolean; children: ReactNode }) => open ? <div role="dialog">{children}</div> : null,
  DialogContent: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogDescription: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  DialogFooter: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogHeader: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: ReactNode }) => <h2>{children}</h2>
}));
vi.mock("@/components/ui/field", () => ({
  Field: ({ children, ...props }: HTMLAttributes<HTMLDivElement>) => <div {...props}>{children}</div>,
  FieldError: ({ children }: { children: ReactNode }) => <p>{children}</p>,
  FieldLabel: ({ children, htmlFor }: { children: ReactNode; htmlFor?: string }) => <label htmlFor={htmlFor}>{children}</label>
}));
vi.mock("@/components/ui/input", () => ({
  Input: (props: InputHTMLAttributes<HTMLInputElement>) => <input {...props} />
}));
vi.mock("../shared/grade-section-picker", () => ({
  GradeSectionPicker: () => <div data-testid="grade-section-picker" />
}));

afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});

describe("PublishDialog", () => {
  it("keeps the division input available when the exam has no courses", () => {
    const document = {
      schemaVersion: 1,
      code: "EX-1",
      title: "Examen",
      courses: [],
      questions: []
    } as ExamDocument;

    render(
      <PublishDialog
        open
        onOpenChange={vi.fn()}
        examId="exam-1"
        versionId="version-1"
        versionNumber={1}
        document={document}
        onSaveBeforePublish={vi.fn().mockResolvedValue(true)}
        onPublished={vi.fn()}
      />
    );

    const divisionInput = screen.getByLabelText("División (opcional)");
    fireEvent.change(divisionInput, { target: { value: "C" } });

    expect((divisionInput as HTMLInputElement).value).toBe("C");
    expect(screen.queryByTestId("grade-section-picker")).toBeNull();
    expect(mocks.callCentral).not.toHaveBeenCalled();
  });
});
