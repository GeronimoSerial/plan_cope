// @vitest-environment jsdom

import type { ButtonHTMLAttributes, HTMLAttributes, InputHTMLAttributes, ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { CreateExamButton } from "./create-exam-dialog";

const mocks = vi.hoisted(() => ({
  callCentral: vi.fn(),
  push: vi.fn(),
  toastSuccess: vi.fn()
}));

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: mocks.push }) }));
vi.mock("sonner", () => ({ toast: { success: mocks.toastSuccess } }));
vi.mock("lucide-react", () => ({ Plus: () => null }));
vi.mock("../../_lib/api/client", () => ({ callCentral: mocks.callCentral }));
vi.mock("@/components/ui/button", () => ({
  Button: ({ children, ...props }: ButtonHTMLAttributes<HTMLButtonElement>) => <button {...props}>{children}</button>
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
  FieldGroup: ({ children, ...props }: HTMLAttributes<HTMLDivElement>) => <div {...props}>{children}</div>,
  FieldLabel: ({ children, htmlFor }: { children: ReactNode; htmlFor?: string }) => <label htmlFor={htmlFor}>{children}</label>
}));
vi.mock("@/components/ui/input", () => ({
  Input: (props: InputHTMLAttributes<HTMLInputElement>) => <input {...props} />
}));
vi.mock("../shared/grade-section-picker", () => ({
  GradeSectionPicker: ({ onValueChange }: { onValueChange: (value: string[]) => void }) => (
    <button type="button" onClick={() => onValueChange(["secundaria-1"])}>Seleccionar curso</button>
  )
}));

afterEach(() => {
  cleanup();
  vi.resetAllMocks();
});

describe("CreateExamButton", () => {
  it("creates an exam from title and course without showing or sending a code", async () => {
    mocks.callCentral.mockResolvedValue({ id: "exam-1", initialVersionId: "version-1" });
    render(<CreateExamButton canEditExams />);

    fireEvent.click(screen.getByRole("button", { name: "Nuevo examen" }));
    expect(screen.queryByLabelText("Código")).toBeNull();
    fireEvent.change(screen.getByLabelText("Título"), { target: { value: "Evaluación de ciencias" } });
    fireEvent.click(screen.getByRole("button", { name: "Seleccionar curso" }));
    fireEvent.click(screen.getByRole("button", { name: "Crear examen" }));

    await waitFor(() => expect(mocks.callCentral).toHaveBeenCalledOnce());
    const [, request] = mocks.callCentral.mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(String(request.body))).toEqual({
      title: "Evaluación de ciencias",
      description: null,
      courses: ["secundaria-1"],
      area: null,
      subject: null
    });
    expect(mocks.push).toHaveBeenCalledWith("/exams/exam-1/versions/version-1/builder");
  });
});
