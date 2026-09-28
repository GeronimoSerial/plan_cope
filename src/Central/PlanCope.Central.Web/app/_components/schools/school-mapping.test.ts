import { describe, it, expect } from "vitest";
import {
  buildCreateSchoolPayload,
  buildUpdateSchoolPayload,
  canOfferSchoolCreate,
  canOfferSchoolEdit,
  formatAnnex,
  schoolsEmptyStateDescription
} from "./school-mapping";
import type { UserProfile } from "../../_lib/contracts";

const admin: UserProfile = {
  id: "u-admin",
  displayName: "Admin",
  role: "Admin",
  rosterScope: "school",
  rosterCues: []
};

const provinceUser: UserProfile = {
  id: "u-province",
  displayName: "Coordinación provincial",
  role: "Teacher",
  rosterScope: "province",
  rosterCues: []
};

const schoolUser: UserProfile = {
  id: "u-school",
  displayName: "Encargado de escuela",
  role: "Teacher",
  rosterScope: "school",
  rosterCues: ["180000100"]
};

const schoolUserNoCues: UserProfile = {
  id: "u-school-empty",
  displayName: "Escuela sin cues",
  role: "Teacher",
  rosterScope: "school",
  rosterCues: []
};

describe("buildCreateSchoolPayload", () => {
  const base = {
    cue: "180-000-100",
    code: "EESC-0100",
    name: "Escuela Nº 100",
    localityId: "02000"
  };

  it("normaliza el CUE quitando guiones y espacios", () => {
    expect(buildCreateSchoolPayload({ ...base, annex: "" }).cue).toBe("180000100");
    expect(buildCreateSchoolPayload({ ...base, cue: " 180 000 100 ", annex: "" }).cue).toBe("180000100");
    expect(buildCreateSchoolPayload({ ...base, cue: "180000100", annex: "" }).cue).toBe("180000100");
  });

  it("convierte un annex vacío en null y no en NaN ni en cadena vacía", () => {
    const payload = buildCreateSchoolPayload({ ...base, annex: "" });
    expect(payload.annex).toBeNull();
    expect(payload.annex).not.toBeNaN();
    expect(JSON.parse(JSON.stringify(payload))).toStrictEqual({
      cue: "180000100",
      code: "EESC-0100",
      name: "Escuela Nº 100",
      localityId: "02000",
      annex: null
    });
  });

  it("convierte el annex a número cuando trae valor", () => {
    expect(buildCreateSchoolPayload({ ...base, annex: "2" }).annex).toBe(2);
    expect(buildCreateSchoolPayload({ ...base, annex: " 3 " }).annex).toBe(3);
    expect(buildCreateSchoolPayload({ ...base, annex: 2 }).annex).toBe(2);
  });

  it("usa null cuando el annex no viene informado", () => {
    expect(buildCreateSchoolPayload(base).annex).toBeNull();
  });
});

describe("buildUpdateSchoolPayload", () => {
  it("arma solo nombre, anexo y localidad", () => {
    const payload = buildUpdateSchoolPayload({
      name: "Escuela Nº 100",
      localityId: "02000",
      annex: ""
    });
    expect(payload).toStrictEqual({
      name: "Escuela Nº 100",
      annex: null,
      localityId: "02000"
    });
  });
});

describe("formatAnnex", () => {
  it("muestra un guión cuando el anexo no existe", () => {
    expect(formatAnnex(null)).toBe("—");
    expect(formatAnnex(undefined)).toBe("—");
  });

  it("muestra el número tal cual", () => {
    expect(formatAnnex(3)).toBe("3");
    expect(formatAnnex(0)).toBe("0");
  });
});

describe("canOfferSchoolCreate", () => {
  it("ofrece crear escuela a un usuario con alcance ilimitado", () => {
    expect(canOfferSchoolCreate(admin)).toBe(true);
    expect(canOfferSchoolCreate(provinceUser)).toBe(true);
  });

  it("no ofrece crear escuela a un usuario con alcance de escuela", () => {
    expect(canOfferSchoolCreate(schoolUser)).toBe(false);
    expect(canOfferSchoolCreate(schoolUserNoCues)).toBe(false);
  });
});

describe("canOfferSchoolEdit", () => {
  it("no ofrece editar una escuela fuera del alcance del usuario", () => {
    expect(canOfferSchoolEdit(schoolUser, "999999999")).toBe(false);
    expect(canOfferSchoolEdit(schoolUserNoCues, "180000100")).toBe(false);
  });

  it("ofrece editar cuando hay alcance sobre el CUE", () => {
    expect(canOfferSchoolEdit(schoolUser, "180000100")).toBe(true);
    expect(canOfferSchoolEdit(schoolUser, "180-000-100")).toBe(true);
    expect(canOfferSchoolEdit(admin, "999999999")).toBe(true);
  });
});

describe("schoolsEmptyStateDescription", () => {
  it("invita a crear cuando el usuario puede crear escuelas", () => {
    expect(schoolsEmptyStateDescription(admin)).toBe("Creá una escuela para empezar.");
    expect(schoolsEmptyStateDescription(provinceUser)).toBe("Creá una escuela para empezar.");
  });

  it("usa un mensaje neutro cuando el usuario no puede crear escuelas", () => {
    expect(schoolsEmptyStateDescription(schoolUser)).toBe("No hay escuelas para tu alcance.");
    expect(schoolsEmptyStateDescription(schoolUserNoCues)).toBe("No hay escuelas para tu alcance.");
  });
});
