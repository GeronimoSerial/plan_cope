import { describe, expect, it } from "vitest";
import {
  filterExams,
  formatPublishedAt,
  isValidExamCode,
  publicationStateBadgeVariant,
  publicationStateLabel,
  publishBlockedMessage,
  validateCreateExam,
  versionStateLabel
} from "./exam-state";
import type { ExamSummary } from "../contracts";

function exam(overrides: Partial<ExamSummary> & Pick<ExamSummary, "id" | "code" | "title">): ExamSummary {
  return {
    status: "Draft",
    versionCount: 1,
    ...overrides
  };
}

describe("publicationStateLabel", () => {
  it("traduce los estados conocidos", () => {
    expect(publicationStateLabel("draft")).toBe("Borrador");
    expect(publicationStateLabel("ready_to_publish")).toBe("Listo para publicar");
    expect(publicationStateLabel("published")).toBe("Publicado");
  });

  it("cae a Borrador con estado desconocido o ausente", () => {
    expect(publicationStateLabel("archived")).toBe("Borrador");
    expect(publicationStateLabel(undefined)).toBe("Borrador");
    expect(publicationStateLabel(null)).toBe("Borrador");
  });
});

describe("publicationStateBadgeVariant", () => {
  it("distingue la variante por estado", () => {
    expect(publicationStateBadgeVariant("published")).toBe("default");
    expect(publicationStateBadgeVariant("ready_to_publish")).toBe("outline");
    expect(publicationStateBadgeVariant("draft")).toBe("secondary");
    expect(publicationStateBadgeVariant(undefined)).toBe("secondary");
  });
});

describe("versionStateLabel", () => {
  it("traduce el estado de la versión", () => {
    expect(versionStateLabel("Published")).toBe("Publicada");
    expect(versionStateLabel("Draft")).toBe("Borrador");
    expect(versionStateLabel("published")).toBe("Publicada");
  });

  it("nunca muestra el valor crudo en inglés", () => {
    expect(versionStateLabel(undefined)).toBe("Borrador");
    expect(versionStateLabel("")).toBe("Borrador");
  });
});

describe("formatPublishedAt", () => {
  it("formatea en es-AR", () => {
    expect(formatPublishedAt("2026-09-28T15:00:00Z")).toBe("28/09/2026");
  });

  it("devuelve vacío con valor ausente o inválido", () => {
    expect(formatPublishedAt(null)).toBe("");
    expect(formatPublishedAt(undefined)).toBe("");
    expect(formatPublishedAt("no-es-fecha")).toBe("");
  });
});

describe("filterExams", () => {
  const exams = [
    exam({ id: "1", code: "MAT-2026-01", title: "Matemática · Primer Año" }),
    exam({ id: "2", code: "LEN-2026-02", title: "Prácticas del Lenguaje" }),
    exam({ id: "3", code: "FIS-2026-03", title: "Física" })
  ];

  it("devuelve todo con búsqueda vacía", () => {
    expect(filterExams(exams, "  ")).toHaveLength(3);
  });

  it("filtra por título sin distinguir mayúsculas ni acentos", () => {
    expect(filterExams(exams, "matematica").map(item => item.id)).toEqual(["1"]);
    expect(filterExams(exams, "PRACTICAS").map(item => item.id)).toEqual(["2"]);
  });

  it("filtra por código", () => {
    expect(filterExams(exams, "fis-2026").map(item => item.id)).toEqual(["3"]);
  });

  it("devuelve vacío sin coincidencias", () => {
    expect(filterExams(exams, "química")).toEqual([]);
  });
});

describe("publishBlockedMessage", () => {
  it("traduce cada motivo", () => {
    expect(publishBlockedMessage("already_published")).toBe("Esta versión ya está publicada");
    expect(publishBlockedMessage("no_blocks")).toBe("Agregá al menos una pregunta");
  });

  it("devuelve vacío si no hay motivo", () => {
    expect(publishBlockedMessage(null)).toBe("");
    expect(publishBlockedMessage(undefined)).toBe("");
  });
});



describe("validateCreateExam", () => {
  it("acepta código y título válidos", () => {
    expect(validateCreateExam({ code: "MAT-2026_01", title: "Examen" })).toEqual({});
  });

  it("exige código y título", () => {
    const errors = validateCreateExam({ code: "  ", title: "" });
    expect(errors.code).toBe("El código es requerido.");
    expect(errors.title).toBe("El título es requerido.");
  });

  it("rechaza un código con caracteres inválidos", () => {
    const errors = validateCreateExam({ code: "MAT 2026/01", title: "Examen" });
    expect(errors.code).toBe("El código solo admite letras, números, guiones y guiones bajos.");
  });

  it("rechaza un código mayor a 64 caracteres", () => {
    const errors = validateCreateExam({ code: "A".repeat(65), title: "Examen" });
    expect(errors.code).toBe("El código no puede superar los 64 caracteres.");
  });

  it("isValidExamCode espeja la regla del backend", () => {
    expect(isValidExamCode("MAT-2026_01")).toBe(true);
    expect(isValidExamCode("  ")).toBe(false);
    expect(isValidExamCode("con espacios")).toBe(false);
  });
});
