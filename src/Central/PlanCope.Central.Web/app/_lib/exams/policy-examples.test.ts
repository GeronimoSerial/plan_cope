import { describe, it, expect } from "vitest";
import { exampleScore, formatScore, referenceExample } from "./policy-examples";
import type { ScoringPolicy } from "../schema/exam";

const policies: ScoringPolicy[] = ["AllOrNothing", "ProportionalPenalised", "ProportionalPlain"];

describe("exampleScore", () => {
  it("puntúa el caso de referencia (1 correcta, 1 incorrecta de 2)", () => {
    expect(exampleScore("AllOrNothing", referenceExample)).toBe(0);
    expect(exampleScore("ProportionalPenalised", referenceExample)).toBe(0);
    expect(exampleScore("ProportionalPlain", referenceExample)).toBe(0.5);
  });

  it("AllOrNothing solo da el puntaje con coincidencia exacta", () => {
    expect(exampleScore("AllOrNothing", { correctTotal: 2, correctMarked: 2, incorrectMarked: 0 })).toBe(1);
    // Marca todas (incluye incorrectas): no es coincidencia exacta.
    expect(exampleScore("AllOrNothing", { correctTotal: 2, correctMarked: 2, incorrectMarked: 2 })).toBe(0);
  });

  it("ProportionalPlain premia las correctas y no penaliza las incorrectas", () => {
    expect(exampleScore("ProportionalPlain", { correctTotal: 2, correctMarked: 2, incorrectMarked: 2 })).toBe(1);
    expect(exampleScore("ProportionalPlain", { correctTotal: 4, correctMarked: 1, incorrectMarked: 0 })).toBe(0.25);
  });

  it("ProportionalPenalised resta las incorrectas sin bajar de 0", () => {
    expect(exampleScore("ProportionalPenalised", { correctTotal: 2, correctMarked: 2, incorrectMarked: 0 })).toBe(1);
    expect(exampleScore("ProportionalPenalised", { correctTotal: 2, correctMarked: 1, incorrectMarked: 2 })).toBe(0);
  });

  it("devuelve 0 cuando no se marcó nada", () => {
    for (const policy of policies) {
      expect(exampleScore(policy, { correctTotal: 2, correctMarked: 0, incorrectMarked: 0 })).toBe(0);
    }
  });

  it("devuelve 0 cuando no hay respuestas correctas", () => {
    for (const policy of policies) {
      expect(exampleScore(policy, { correctTotal: 0, correctMarked: 0, incorrectMarked: 1 })).toBe(0);
    }
  });
});

describe("formatScore", () => {
  it("usa coma decimal es-AR", () => {
    expect(formatScore(0.5)).toBe("0,5");
    expect(formatScore(0)).toBe("0");
    expect(formatScore(1)).toBe("1");
    expect(formatScore(1 / 3)).toBe("0,33");
  });
});
