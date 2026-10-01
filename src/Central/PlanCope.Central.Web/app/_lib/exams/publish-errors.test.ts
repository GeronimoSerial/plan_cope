import { describe, it, expect } from "vitest";
import { mapPublishError, parsePublishError, publishErrorMessage } from "./publish-errors";

describe("mapPublishError", () => {
  it("mapea 409 a versión ya publicada", () => {
    expect(mapPublishError({ status: 409 })).toBe("Esta versión ya está publicada.");
  });

  it("mapea 400 con clave blocks", () => {
    expect(
      mapPublishError({ status: 400, problem: { errors: { blocks: ["At least one block is required."] } } })
    ).toBe("Agregá al menos una pregunta.");
  });



  it("mapea 400 con clave courses", () => {
    expect(mapPublishError({ status: 400, problem: { errors: { Courses: ["At least one valid course is required."] } } })).toBe(
      "Seleccioná al menos un curso en los datos del examen."
    );
  });

  it("mapea otras claves de bloque con el prefijo Revisá las preguntas", () => {
    expect(
      mapPublishError({ status: 400, problem: { errors: { "config.options": ["multiple_choice requires at least two options."] } } })
    ).toBe("Revisá las preguntas: multiple_choice requires at least two options.");
  });

  it("mapea 404 a versión no encontrada", () => {
    expect(mapPublishError({ status: 404 })).toBe("No se encontró la versión.");
  });

  it("mapea 5xx y errores de red al mensaje genérico", () => {
    expect(mapPublishError({ status: 500 })).toBe("No se pudo publicar. Probá de nuevo.");
    expect(mapPublishError({ status: null, message: "Failed to fetch" })).toBe(
      "No se pudo publicar. Probá de nuevo."
    );
  });

  it("es genérico cuando no hay información", () => {
    expect(mapPublishError({})).toBe("No se pudo publicar. Probá de nuevo.");
  });
});

describe("parsePublishError", () => {
  it("reconstruye 409 desde el texto de callCentral", () => {
    const input = parsePublishError(new Error('"Exam version is already published."'));
    expect(input.status).toBe(409);
    expect(mapPublishError(input)).toBe("Esta versión ya está publicada.");
  });

  it("reconstruye 404 desde el mensaje de estado", () => {
    expect(mapPublishError(parsePublishError(new Error("El servicio respondió con estado 404.")))).toBe(
      "No se encontró la versión."
    );
  });

  it("reconoce la validación de bloques por texto", () => {
    expect(
      publishErrorMessage(new Error("At least one block is required before publishing."))
    ).toBe("Agregá al menos una pregunta.");
    expect(
      publishErrorMessage(new Error("multiple_choice requires at least two options."))
    ).toBe("Revisá las preguntas: multiple_choice requires at least two options.");
  });

  it("trata un error de red como genérico", () => {
    expect(publishErrorMessage(new TypeError("Failed to fetch"))).toBe("No se pudo publicar. Probá de nuevo.");
  });
});
