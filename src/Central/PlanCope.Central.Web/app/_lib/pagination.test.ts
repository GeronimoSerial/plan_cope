import { describe, it, expect } from "vitest";
import {
  PAGE_SIZE,
  buildPageHref,
  buildPageWindow,
  filterByQuery,
  formatShowing,
  matchesQuery,
  normalizeText,
  paginate,
  parsePageParams
} from "./pagination";

describe("parsePageParams", () => {
  it("usa valores por defecto cuando no hay parámetros", () => {
    expect(parsePageParams(undefined)).toStrictEqual({ q: "", page: 1, pageSize: PAGE_SIZE });
    expect(parsePageParams({})).toStrictEqual({ q: "", page: 1, pageSize: PAGE_SIZE });
  });

  it("lee y recorta q", () => {
    expect(parsePageParams({ q: "  escuela 12 " }).q).toBe("escuela 12");
    expect(parsePageParams({ q: ["primero", "segundo"] }).q).toBe("primero");
  });

  it("cae a la página 1 ante valores inválidos", () => {
    expect(parsePageParams({ page: "0" }).page).toBe(1);
    expect(parsePageParams({ page: "-3" }).page).toBe(1);
    expect(parsePageParams({ page: "abc" }).page).toBe(1);
    expect(parsePageParams({ page: "" }).page).toBe(1);
  });

  it("acepta una página válida", () => {
    expect(parsePageParams({ page: "7" }).page).toBe(7);
  });

  it("mantiene el tamaño de página fijo en 25", () => {
    expect(parsePageParams({ pageSize: "1000" }).pageSize).toBe(PAGE_SIZE);
    expect(parsePageParams(undefined).pageSize).toBe(25);
  });
});

describe("normalizeText", () => {
  it("quita acentos, mayúsculas y espacios externos", () => {
    expect(normalizeText("  Escuela San José  ")).toBe("escuela san jose");
    expect(normalizeText("ÁÉÍÓÚ")).toBe("aeiou");
    expect(normalizeText(null)).toBe("");
    expect(normalizeText(undefined)).toBe("");
  });
});

describe("matchesQuery / filterByQuery", () => {
  const schools = [
    { cue: "180000100", name: "Escuela Nº 12" },
    { cue: "180000200", name: "Instituto San José" },
    { cue: "060000300", name: "Colegio Nacional" }
  ];

  it("una consulta vacía devuelve todos", () => {
    expect(matchesQuery("", ["algo"])).toBe(true);
    expect(filterByQuery(schools, "   ", item => [item.cue, item.name])).toHaveLength(3);
  });

  it("filtra por CUE y por nombre", () => {
    expect(filterByQuery(schools, "180000200", item => [item.cue, item.name])).toHaveLength(1);
    expect(filterByQuery(schools, "colegio", item => [item.cue, item.name])).toHaveLength(1);
  });

  it("ignora mayúsculas y acentos", () => {
    expect(filterByQuery(schools, "JOSE", item => [item.cue, item.name])).toHaveLength(1);
    expect(filterByQuery(schools, "josé", item => [item.cue, item.name])).toHaveLength(1);
    expect(filterByQuery(schools, "INSTITUTO", item => [item.cue, item.name])).toHaveLength(1);
  });
});

describe("paginate", () => {
  const items = Array.from({ length: 60 }, (_, index) => index + 1);

  it("devuelve la primera página con los índices visibles", () => {
    const result = paginate(items, 1, 25);
    expect(result.items).toHaveLength(25);
    expect(result.page).toBe(1);
    expect(result.total).toBe(60);
    expect(result.totalPages).toBe(3);
    expect(result.from).toBe(1);
    expect(result.to).toBe(25);
  });

  it("devuelve la última página parcial", () => {
    const result = paginate(items, 3, 25);
    expect(result.items).toHaveLength(10);
    expect(result.from).toBe(51);
    expect(result.to).toBe(60);
  });

  it("clampea una página fuera de rango", () => {
    expect(paginate(items, 99, 25).page).toBe(3);
    expect(paginate(items, 0, 25).page).toBe(1);
  });

  it("maneja listas vacías en una sola página", () => {
    const result = paginate([], 5, 25);
    expect(result.items).toHaveLength(0);
    expect(result.page).toBe(1);
    expect(result.totalPages).toBe(1);
    expect(result.from).toBe(0);
    expect(result.to).toBe(0);
  });
});

describe("formatShowing", () => {
  it("muestra el rango con en dash", () => {
    expect(formatShowing(1, 25, 2058)).toBe("Mostrando 1–25 de 2058");
    expect(formatShowing(2051, 2058, 2058)).toBe("Mostrando 2051–2058 de 2058");
  });

  it("usa un texto corto sin resultados", () => {
    expect(formatShowing(0, 0, 0)).toBe("Sin resultados");
  });
});

describe("buildPageWindow", () => {
  it("incluye siempre primera y última página", () => {
    expect(buildPageWindow(1, 10)).toStrictEqual([1, 2, "ellipsis", 10]);
    expect(buildPageWindow(10, 10)).toStrictEqual([1, "ellipsis", 9, 10]);
    expect(buildPageWindow(5, 10)).toStrictEqual([1, "ellipsis", 4, 5, 6, "ellipsis", 10]);
  });

  it("no agrega elipsis en ventanas contiguas", () => {
    expect(buildPageWindow(1, 3)).toStrictEqual([1, 2, 3]);
    expect(buildPageWindow(2, 3)).toStrictEqual([1, 2, 3]);
  });

  it("maneja una sola página o ninguna", () => {
    expect(buildPageWindow(1, 1)).toStrictEqual([1]);
    expect(buildPageWindow(1, 0)).toStrictEqual([]);
  });
});

describe("buildPageHref", () => {
  it("omite la página 1 y los parámetros vacíos", () => {
    expect(buildPageHref("/escuelas", "", 1)).toBe("/escuelas");
    expect(buildPageHref("/escuelas", "san jose", 1)).toBe("/escuelas?q=san+jose");
    expect(buildPageHref("/escuelas", "san jose", 3)).toBe("/escuelas?q=san+jose&page=3");
    expect(buildPageHref("/escuelas", "", 4)).toBe("/escuelas?page=4");
  });
});
