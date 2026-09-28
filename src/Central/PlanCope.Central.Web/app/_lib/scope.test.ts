import { describe, it, expect } from "vitest";
import {
  CUE_LENGTH,
  normalizeCue,
  hasUnboundedScope,
  hasCueScope,
  canCreateSchool,
  canEditSchool,
  canCreateUser,
  canManageUser,
  UNBOUNDED_SCOPE_ROLES,
  canAssignRole,
  canRevokeRole,
  canAssignOrRevokeCue
} from "./scope";
import type { UserProfile } from "./contracts";

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

describe("normalizeCue", () => {
  it("acepta un CUE de 9 digitos", () => {
    expect(normalizeCue("180000100")).toBe("180000100");
    expect(CUE_LENGTH).toBe(9);
  });

  it("quita guiones y espacios y devuelve 9 digitos", () => {
    expect(normalizeCue("180-000-100")).toBe("180000100");
    expect(normalizeCue(" 180 000 100 ")).toBe("180000100");
  });

  it("rechaza menos de 9 digitos", () => {
    expect(normalizeCue("18000010")).toBeNull();
    expect(normalizeCue("")).toBeNull();
    expect(normalizeCue("   ")).toBeNull();
  });

  it("rechaza mas de 9 digitos", () => {
    expect(normalizeCue("1800001001")).toBeNull();
    expect(normalizeCue("1-800001001")).toBeNull();
  });

  it("rechaza strings no numericos", () => {
    expect(normalizeCue("abcdefghi")).toBeNull();
    expect(normalizeCue("18000010a")).toBeNull();
  });

  it("rechaza caracteres que no sean digitos, guiones ni espacios", () => {
    expect(normalizeCue("18.000.010-0")).toBeNull();
  });
});

describe("hasUnboundedScope", () => {
  it("es true para Admin aunque el rosterScope sea school", () => {
    expect(hasUnboundedScope({ role: "Admin", rosterScope: "school" })).toBe(true);
  });

  it("es true para rosterScope province", () => {
    expect(hasUnboundedScope({ role: "Teacher", rosterScope: "province" })).toBe(true);
  });

  it("es false para Teacher con rosterScope school", () => {
    expect(hasUnboundedScope({ role: "Teacher", rosterScope: "school" })).toBe(false);
  });
});

describe("hasCueScope", () => {
  it("es true para scope ilimitado sin importar el cue", () => {
    expect(hasCueScope(admin, "999999999")).toBe(true);
    expect(hasCueScope(admin, "cue-invalido")).toBe(true);
    expect(hasCueScope(provinceUser, "999999999")).toBe(true);
  });

  it("es true para rosterScope school cuando el cue normalizado esta en rosterCues", () => {
    expect(hasCueScope(schoolUser, "180000100")).toBe(true);
    expect(hasCueScope(schoolUser, "180-000-100")).toBe(true);
    expect(hasCueScope(schoolUser, " 180 000 100 ")).toBe(true);
  });

  it("es false para rosterScope school cuando el cue no esta en rosterCues", () => {
    expect(hasCueScope(schoolUser, "999999999")).toBe(false);
    expect(hasCueScope(schoolUser, "999-999-999")).toBe(false);
  });

  it("es false para rosterScope school sin rosterCues", () => {
    expect(hasCueScope(schoolUserNoCues, "180000100")).toBe(false);
  });
});

describe("acciones de escuela y alta de usuarios", () => {
  it("canCreateSchool solo con scope ilimitado", () => {
    expect(canCreateSchool(admin)).toBe(true);
    expect(canCreateSchool(provinceUser)).toBe(true);
    expect(canCreateSchool(schoolUser)).toBe(false);
  });

  it("canEditSchool delega en hasCueScope", () => {
    expect(canEditSchool(schoolUser, "180-000-100")).toBe(true);
    expect(canEditSchool(schoolUser, "999999999")).toBe(false);
    expect(canEditSchool(schoolUserNoCues, "180000100")).toBe(false);
    expect(canEditSchool(admin, "999999999")).toBe(true);
  });

  it("canCreateUser solo con scope ilimitado", () => {
    expect(canCreateUser(admin)).toBe(true);
    expect(canCreateUser(schoolUser)).toBe(false);
  });
});

describe("canManageUser", () => {
  it("es true para scope ilimitado aunque el usuario no tenga cues asignados", () => {
    expect(canManageUser(admin, [])).toBe(true);
    expect(canManageUser(provinceUser, [])).toBe(true);
  });

  it("es true para rosterScope school con al menos un cue en comun", () => {
    expect(canManageUser(schoolUser, ["180000100"])).toBe(true);
    expect(canManageUser(schoolUser, ["999999999", "180-000-100"])).toBe(true);
  });

  it("es false para rosterScope school sin cues asignados en el usuario destino", () => {
    expect(canManageUser(schoolUser, [])).toBe(false);
  });

  it("es false para rosterScope school sin solapamiento de cues", () => {
    expect(canManageUser(schoolUser, ["999999999"])).toBe(false);
    expect(canManageUser(schoolUserNoCues, ["180000100"])).toBe(false);
  });
});

describe("canAssignRole", () => {
  it("es false para rol restringido aunque el caller pueda gestionar al usuario", () => {
    expect(canManageUser(schoolUser, ["180000100"])).toBe(true);
    expect(canAssignRole(schoolUser, ["180000100"], "Admin")).toBe(false);
    expect(canAssignRole(schoolUser, ["180000100"], "RosterProvince")).toBe(false);
  });

  it("es true para un rol no restringido con el mismo caller", () => {
    expect(canAssignRole(schoolUser, ["180000100"], "Teacher")).toBe(true);
  });

  it("es true para un caller con scope ilimitado asignando Admin", () => {
    expect(canAssignRole(admin, [], "Admin")).toBe(true);
    expect(canAssignRole(provinceUser, ["180000100"], "Admin")).toBe(true);
  });

  it("es false cuando el caller no puede gestionar al usuario", () => {
    expect(canAssignRole(schoolUser, ["999999999"], "Teacher")).toBe(false);
  });

  it("expone los roles que exigen scope ilimitado", () => {
    expect(UNBOUNDED_SCOPE_ROLES.has("Admin")).toBe(true);
    expect(UNBOUNDED_SCOPE_ROLES.has("RosterProvince")).toBe(true);
    expect(UNBOUNDED_SCOPE_ROLES.has("Teacher")).toBe(false);
  });
});

describe("canRevokeRole", () => {
  it("no aplica la restriccion de rol: un caller school puede revocar Admin", () => {
    expect(canAssignRole(schoolUser, ["180000100"], "Admin")).toBe(false);
    expect(canRevokeRole(schoolUser, ["180000100"])).toBe(true);
  });

  it("es false cuando el caller no puede gestionar al usuario", () => {
    expect(canRevokeRole(schoolUser, [])).toBe(false);
    expect(canRevokeRole(schoolUser, ["999999999"])).toBe(false);
  });
});

describe("canAssignOrRevokeCue", () => {
  it("es true cuando el caller tiene scope sobre el cue", () => {
    expect(canAssignOrRevokeCue(schoolUser, "180000100")).toBe(true);
    expect(canAssignOrRevokeCue(schoolUser, "180-000-100")).toBe(true);
    expect(canAssignOrRevokeCue(admin, "999999999")).toBe(true);
  });

  it("es false sin scope sobre ese cue aunque pueda gestionar al usuario", () => {
    expect(canManageUser(schoolUser, ["180000100"])).toBe(true);
    expect(canAssignOrRevokeCue(schoolUser, "999999999")).toBe(false);
    expect(canAssignOrRevokeCue(schoolUserNoCues, "180000100")).toBe(false);
  });
});
