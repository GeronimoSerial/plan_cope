import { describe, it, expect } from "vitest";
import {
  applyAssignmentResult,
  assignButtonLabel,
  assignedToastMessage,
  confirmDescription,
  formatPublishedDate,
  isAllSelected,
  isSomeSelected,
  rejectedAlertMessage,
  selectionHint,
  toggleAll,
  toggleOne
} from "./legacy-policy";

describe("formatPublishedDate", () => {
  it("formatea en dd/mm/aaaa", () => {
    expect(formatPublishedDate("2026-03-05T12:00:00Z")).toBe("05/03/2026");
  });

  it("devuelve un guion cuando no hay fecha o es inválida", () => {
    expect(formatPublishedDate(null)).toBe("—");
    expect(formatPublishedDate(undefined)).toBe("—");
    expect(formatPublishedDate("")).toBe("—");
    expect(formatPublishedDate("no-es-fecha")).toBe("—");
  });
});

describe("toggleOne", () => {
  it("agrega el id cuando se marca", () => {
    expect(toggleOne(["a"], "b", true)).toStrictEqual(["a", "b"]);
  });

  it("no duplica un id ya seleccionado", () => {
    expect(toggleOne(["a", "b"], "a", true)).toStrictEqual(["a", "b"]);
  });

  it("quita el id cuando se desmarca", () => {
    expect(toggleOne(["a", "b"], "a", false)).toStrictEqual(["b"]);
  });
});

describe("toggleAll", () => {
  it("selecciona todos los ids o ninguno", () => {
    expect(toggleAll(["a", "b"], true)).toStrictEqual(["a", "b"]);
    expect(toggleAll(["a", "b"], false)).toStrictEqual([]);
  });
});

describe("isAllSelected / isSomeSelected", () => {
  it("detecta la selección completa", () => {
    expect(isAllSelected(3, 3)).toBe(true);
    expect(isAllSelected(0, 0)).toBe(false);
    expect(isAllSelected(2, 3)).toBe(false);
  });

  it("detecta la selección parcial", () => {
    expect(isSomeSelected(1, 3)).toBe(true);
    expect(isSomeSelected(0, 3)).toBe(false);
    expect(isSomeSelected(3, 3)).toBe(false);
  });
});

describe("pluralización", () => {
  it("usa singular con una sola versión", () => {
    expect(assignButtonLabel(1)).toBe("Asignar a 1 versión");
    expect(assignedToastMessage(1)).toBe("Se asignó la regla a 1 versión");
    expect(rejectedAlertMessage(1)).toBe("1 versión fue rechazada");
    expect(selectionHint(1)).toBe("1 versión seleccionada.");
  });

  it("usa solo el verbo sin selección", () => {
    expect(assignButtonLabel(0)).toBe("Asignar");
  });

  it("usa plural con varias versiones", () => {
    expect(assignButtonLabel(3)).toBe("Asignar a 3 versiones");
    expect(assignedToastMessage(3)).toBe("Se asignó la regla a 3 versiones");
    expect(rejectedAlertMessage(2)).toBe("2 versiones fueron rechazadas");
    expect(selectionHint(2)).toBe("2 versiones seleccionadas.");
  });

  it("pide selección cuando no hay ninguna", () => {
    expect(selectionHint(0)).toBe("Seleccioná al menos una versión.");
  });
});

describe("confirmDescription", () => {
  it("incluye la regla y la cantidad", () => {
    expect(confirmDescription("Todo o nada", 3)).toBe(
      '¿Asignar la regla "Todo o nada" a 3 versiones? Se aplica a exámenes ya publicados.'
    );
    expect(confirmDescription("Todo o nada", 1)).toBe(
      '¿Asignar la regla "Todo o nada" a 1 versión? Se aplica a exámenes ya publicados.'
    );
  });
});

describe("applyAssignmentResult", () => {
  const versions = [
    { examVersionId: "a" },
    { examVersionId: "b" },
    { examVersionId: "c" }
  ];

  it("quita las asignadas y conserva las rechazadas", () => {
    expect(applyAssignmentResult(versions, ["a", "b", "c"], ["b"])).toStrictEqual([
      { examVersionId: "b" }
    ]);
  });

  it("si nada fue rechazado deja solo las no seleccionadas", () => {
    expect(applyAssignmentResult(versions, ["a"], [])).toStrictEqual([
      { examVersionId: "b" },
      { examVersionId: "c" }
    ]);
  });

  it("no toca versiones fuera de la selección", () => {
    expect(applyAssignmentResult(versions, [], [])).toStrictEqual(versions);
  });
});
