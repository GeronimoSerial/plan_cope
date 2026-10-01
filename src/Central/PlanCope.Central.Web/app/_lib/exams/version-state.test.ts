import { describe, expect, it } from "vitest";
import {
  createVersionConfirmation,
  draftExistsConflict,
  findCurrent,
  findDraft,
  nextVersionNumber,
  primaryEditTarget,
  publishSupersedeMessage,
  versionBuilderHref,
  versionStatusBadgeVariant,
  versionStatusLabel,
  versionStatusLine,
  versionStatusTerm
} from "./version-state";
import type { ExamVersion } from "../contracts";

function version(overrides: Partial<ExamVersion> & Pick<ExamVersion, "id" | "versionNumber" | "status">): ExamVersion {
  return {
    examId: "ex_1",
    schemaVersion: 1,
    blocks: [],
    answerKeys: [],
    assets: [],
    blockCount: 0,
    canPublish: false,
    isCurrent: false,
    ...overrides
  };
}

describe("versionStatusLabel", () => {
  it("etiqueta borrador, publicada actual y reemplazada", () => {
    expect(versionStatusLabel({ status: "Draft", isCurrent: false })).toBe("Borrador");
    expect(versionStatusLabel({ status: "Published", isCurrent: true })).toBe("Publicada");
    expect(versionStatusLabel({ status: "Published", isCurrent: false })).toBe("Reemplazada");
  });

  it("cae a borrador con estado ausente", () => {
    expect(versionStatusLabel({ status: null })).toBe("Borrador");
    expect(versionStatusLabel({ status: "" })).toBe("Borrador");
  });

  it("elige el termino y la variante del badge", () => {
    expect(versionStatusTerm({ status: "Published", isCurrent: false })).toBe("estado-reemplazada");
    expect(versionStatusTerm({ status: "Published", isCurrent: true })).toBe("estado-publicado");
    expect(versionStatusTerm({ status: "Draft" })).toBe("estado-borrador");
    expect(versionStatusBadgeVariant({ status: "Published", isCurrent: false })).toBe("outline");
    expect(versionStatusBadgeVariant({ status: "Published", isCurrent: true })).toBe("default");
    expect(versionStatusBadgeVariant({ status: "Draft" })).toBe("secondary");
  });
});

describe("nextVersionNumber", () => {
  it("es el maximo + 1", () => {
    expect(nextVersionNumber([])).toBe(1);
    expect(nextVersionNumber([version({ id: "a", versionNumber: 3, status: "Published", isCurrent: true })])).toBe(4);
  });
});

describe("findDraft / findCurrent", () => {
  const draftV2 = version({ id: "d2", versionNumber: 2, status: "Draft" });
  const currentV1 = version({ id: "p1", versionNumber: 1, status: "Published", isCurrent: true });

  it("encuentra el borrador y la version actual", () => {
    expect(findDraft([currentV1, draftV2])?.id).toBe("d2");
    expect(findCurrent([currentV1, draftV2])?.id).toBe("p1");
  });

  it("devuelve el borrador de numero mayor si hubiera varios", () => {
    const draftV3 = version({ id: "d3", versionNumber: 3, status: "Draft" });
    expect(findDraft([draftV2, draftV3])?.id).toBe("d3");
  });

  it("devuelve undefined cuando no hay", () => {
    expect(findDraft([currentV1])).toBeUndefined();
    expect(findCurrent([draftV2])).toBeUndefined();
  });

  it("cae a la publicada de numero mayor si falta isCurrent", () => {
    const old = version({ id: "p1", versionNumber: 1, status: "Published" });
    const newer = version({ id: "p2", versionNumber: 2, status: "Published" });
    expect(findCurrent([old, newer])?.id).toBe("p2");
  });
});

describe("primaryEditTarget", () => {
  it("abre el borrador cuando existe", () => {
    const draft = version({ id: "d2", versionNumber: 2, status: "Draft" });
    const current = version({ id: "p1", versionNumber: 1, status: "Published", isCurrent: true });
    expect(primaryEditTarget([current, draft])).toEqual({ kind: "open-draft", versionId: "d2" });
  });

  it("crea una copia de la publicada cuando no hay borrador", () => {
    const current = version({ id: "p1", versionNumber: 1, status: "Published", isCurrent: true });
    expect(primaryEditTarget([current])).toEqual({
      kind: "create-from",
      sourceVersionId: "p1",
      sourceNumber: 1,
      nextNumber: 2
    });
  });

  it("no ofrece nada sin versiones", () => {
    expect(primaryEditTarget([])).toEqual({ kind: "none" });
  });
});

describe("versionStatusLine", () => {
  it("arma la linea del borrador con su origen", () => {
    expect(
      versionStatusLine({ versionNumber: 3, status: "Draft", basedOnVersionNumber: 2 })
    ).toBe("Versión 3 · Borrador (basada en la versión 2)");
  });

  it("omite el origen cuando no se conoce", () => {
    expect(versionStatusLine({ versionNumber: 1, status: "Draft", basedOnVersionNumber: null })).toBe(
      "Versión 1 · Borrador"
    );
  });

  it("no agrega origen a las publicadas", () => {
    expect(
      versionStatusLine({ versionNumber: 2, status: "Published", isCurrent: true, basedOnVersionNumber: 1 })
    ).toBe("Versión 2 · Publicada");
    expect(versionStatusLine({ versionNumber: 1, status: "Published", isCurrent: false })).toBe(
      "Versión 1 · Reemplazada"
    );
  });
});

describe("createVersionConfirmation", () => {
  it("describe la copia de una version publicada", () => {
    expect(
      createVersionConfirmation({ nextNumber: 2, sourceNumber: 1, sourcePublished: true })
    ).toBe(
      "Se va a crear la versión 2 a partir de la versión 1 publicada. La versión publicada no cambia hasta que publiques la nueva."
    );
  });

  it("describe la copia de una version no publicada", () => {
    expect(
      createVersionConfirmation({ nextNumber: 4, sourceNumber: 2, sourcePublished: false })
    ).toBe(
      "Se va a crear la versión 4 a partir de la versión 2. La versión 2 no cambia hasta que publiques la nueva."
    );
  });
});

describe("versionBuilderHref", () => {
  it("arma la ruta del builder de la version", () => {
    expect(versionBuilderHref("ex_1", "ev_2")).toBe("/exams/ex_1/versions/ev_2/builder");
  });
});

describe("draftExistsConflict", () => {
  it("reconoce el 409 draft_exists y devuelve el borrador", () => {
    expect(
      draftExistsConflict({ status: 409, body: { code: "draft_exists", draftVersionId: "ev_draft" } })
    ).toEqual({ draftVersionId: "ev_draft" });
  });

  it("ignora otros estados y otros codigos", () => {
    expect(draftExistsConflict({ status: 400, body: { code: "draft_exists", draftVersionId: "x" } })).toBeNull();
    expect(draftExistsConflict({ status: 409, body: { code: "other", draftVersionId: "x" } })).toBeNull();
  });

  it("ignora cuerpos sin draftVersionId util", () => {
    expect(draftExistsConflict({ status: 409, body: { code: "draft_exists" } })).toBeNull();
    expect(draftExistsConflict({ status: 409, body: { code: "draft_exists", draftVersionId: "" } })).toBeNull();
    expect(draftExistsConflict({ status: 409, body: { code: "draft_exists", draftVersionId: "  " } })).toBeNull();
    expect(draftExistsConflict({ status: 409, body: null })).toBeNull();
  });

  it("ignora errores sin estado ni cuerpo estructurado", () => {
    expect(draftExistsConflict(new Error("boom"))).toBeNull();
    expect(draftExistsConflict(null)).toBeNull();
    expect(draftExistsConflict("409")).toBeNull();
  });
});

describe("publishSupersedeMessage", () => {
  it("avisa cuando reemplaza a una publicada anterior", () => {
    expect(publishSupersedeMessage({ currentPublishedNumber: 1, versionNumber: 2 })).toBe(
      "Reemplaza a la versión 1 en los nodos."
    );
  });

  it("no avisa sin publicada previa ni cuando no es posterior", () => {
    expect(publishSupersedeMessage({ currentPublishedNumber: null, versionNumber: 2 })).toBeNull();
    expect(publishSupersedeMessage({ currentPublishedNumber: 2, versionNumber: 2 })).toBeNull();
    expect(publishSupersedeMessage({ currentPublishedNumber: 3, versionNumber: 2 })).toBeNull();
  });
});
