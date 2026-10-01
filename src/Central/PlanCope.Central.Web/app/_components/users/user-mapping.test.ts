import { describe, it, expect } from "vitest";
import {
  MIN_PASSWORD_LENGTH,
  buildCreateUserPayload,
  canOfferCueRevoke,
  canOfferResetPassword,
  canOfferRoleRevoke,
  canOfferUserCreate,
  canOfferUserDeactivate,
  cuesAvailableToAssign,
  formatCues,
  formatRoleCodes,
  hasAnyRoleOrCueAction,
  roleLabel,
  rolesAvailableToAssign,
  userStatusLabel,
  usersEmptyStateDescription
} from "./user-mapping";
import { canManageUser } from "../../_lib/scope";
import type { UserProfile } from "../../_lib/contracts";
import type { RoleSummary } from "../../_lib/api/server";

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

describe("MIN_PASSWORD_LENGTH", () => {
  it("refleja MinPasswordLength del backend", () => {
    expect(MIN_PASSWORD_LENGTH).toBe(8);
  });
});

describe("buildCreateUserPayload", () => {
  it("arma el body del POST /admin/users con los tres campos", () => {
    expect(
      buildCreateUserPayload({ email: "ana@escuela.edu", password: "secreta123", fullName: "Ana Pérez" })
    ).toStrictEqual({
      email: "ana@escuela.edu",
      password: "secreta123",
      fullName: "Ana Pérez"
    });
  });

  it("no recorta ni transforma la contraseña", () => {
    expect(
      buildCreateUserPayload({ email: "a@b.com", password: "  corta  ", fullName: "A" }).password
    ).toBe("  corta  ");
  });
});

describe("formatCues", () => {
  it("muestra todas las escuelas cuando no hay CUEs asignados", () => {
    expect(formatCues([])).toBe("Todas las escuelas");
  });

  it("une los CUEs con coma y espacio", () => {
    expect(formatCues(["180000100"])).toBe("180000100");
    expect(formatCues(["180000100", "180000200"])).toBe("180000100, 180000200");
  });
});

describe("formatRoleCodes", () => {
  it("muestra un guión cuando no hay roles", () => {
    expect(formatRoleCodes([])).toBe("—");
  });

  it("une los códigos con coma y espacio", () => {
    expect(formatRoleCodes(["Teacher"])).toBe("Teacher");
    expect(formatRoleCodes(["Teacher", "Grader"])).toBe("Teacher, Grader");
  });
});

describe("userStatusLabel", () => {
  it("traduce los estados del backend al español", () => {
    expect(userStatusLabel("Active")).toBe("Activo");
    expect(userStatusLabel("Inactive")).toBe("Inactivo");
  });

  it("ignora mayúsculas y minúsculas", () => {
    expect(userStatusLabel("active")).toBe("Activo");
    expect(userStatusLabel("INACTIVE")).toBe("Inactivo");
  });

  it("deja pasar un estado desconocido tal cual", () => {
    expect(userStatusLabel("Suspended")).toBe("Suspended");
  });
});

describe("canOfferUserCreate", () => {
  it("ofrece crear usuarios con alcance ilimitado", () => {
    expect(canOfferUserCreate(admin)).toBe(true);
    expect(canOfferUserCreate(provinceUser)).toBe(true);
  });

  it("no ofrece crear usuarios con alcance de escuela", () => {
    expect(canOfferUserCreate(schoolUser)).toBe(false);
    expect(canOfferUserCreate(schoolUserNoCues)).toBe(false);
  });
});

describe("canOfferUserDeactivate", () => {
  it("ofrece desactivar cuando el usuario puede gestionar el objetivo y está activo", () => {
    expect(canOfferUserDeactivate(admin, ["180000100"], "Active")).toBe(true);
    expect(canOfferUserDeactivate(provinceUser, [], "Active")).toBe(true);
    expect(canOfferUserDeactivate(schoolUser, ["180000100"], "Active")).toBe(true);
    expect(canOfferUserDeactivate(schoolUser, ["180000100", "999999999"], "Active")).toBe(true);
  });

  it("no ofrece desactivar un usuario que ya está inactivo", () => {
    expect(canOfferUserDeactivate(admin, ["180000100"], "Inactive")).toBe(false);
    expect(canOfferUserDeactivate(schoolUser, ["180000100"], "Inactive")).toBe(false);
  });

  it("no ofrece desactivar un usuario activo fuera del alcance del llamador", () => {
    expect(canOfferUserDeactivate(schoolUser, ["999999999"], "Active")).toBe(false);
    expect(canOfferUserDeactivate(schoolUserNoCues, ["180000100"], "Active")).toBe(false);
  });

  it("no ofrece desactivar un objetivo sin CUEs desde alcance de escuela", () => {
    expect(canOfferUserDeactivate(schoolUser, [], "Active")).toBe(false);
    expect(canOfferUserDeactivate(schoolUserNoCues, [], "Active")).toBe(false);
  });
});

describe("canOfferResetPassword", () => {
  it("ofrece restablecer cuando el usuario puede gestionar el objetivo", () => {
    expect(canOfferResetPassword(admin, ["180000100"])).toBe(true);
    expect(canOfferResetPassword(provinceUser, [])).toBe(true);
    expect(canOfferResetPassword(schoolUser, ["180000100"])).toBe(true);
  });

  it("no ofrece restablecer fuera del alcance del llamador", () => {
    expect(canOfferResetPassword(schoolUser, ["999999999"])).toBe(false);
    expect(canOfferResetPassword(schoolUser, [])).toBe(false);
    expect(canOfferResetPassword(schoolUserNoCues, [])).toBe(false);
  });
});

describe("usersEmptyStateDescription", () => {
  it("invita a crear cuando el usuario puede crear usuarios", () => {
    expect(usersEmptyStateDescription(admin)).toBe("Creá un usuario para empezar.");
    expect(usersEmptyStateDescription(provinceUser)).toBe("Creá un usuario para empezar.");
  });

  it("usa un mensaje neutro cuando el usuario no puede crear usuarios", () => {
    expect(usersEmptyStateDescription(schoolUser)).toBe("No hay usuarios para tu alcance.");
    expect(usersEmptyStateDescription(schoolUserNoCues)).toBe("No hay usuarios para tu alcance.");
  });
});

const roles: Pick<RoleSummary, "code">[] = [
  { code: "Admin" },
  { code: "RosterProvince" },
  { code: "Teacher" },
  { code: "Grader" }
];

describe("rolesAvailableToAssign", () => {
  it("excluye Admin y RosterProvince para un caller de escuela aunque pueda gestionar al objetivo", () => {
    expect(canManageUser(schoolUser, ["180000100"])).toBe(true);
    expect(rolesAvailableToAssign(schoolUser, ["180000100"], [], roles)).toStrictEqual([
      "Teacher",
      "Grader"
    ]);
  });

  it("incluye Admin y RosterProvince para un caller con alcance ilimitado", () => {
    expect(rolesAvailableToAssign(admin, [], [], roles)).toStrictEqual([
      "Admin",
      "RosterProvince",
      "Teacher",
      "Grader"
    ]);
    expect(rolesAvailableToAssign(provinceUser, [], [], roles)).toStrictEqual([
      "Admin",
      "RosterProvince",
      "Teacher",
      "Grader"
    ]);
  });

  it("excluye los roles que el objetivo ya tiene, sin importar el alcance", () => {
    expect(rolesAvailableToAssign(admin, [], ["Admin", "Teacher"], roles)).toStrictEqual([
      "RosterProvince",
      "Grader"
    ]);
    expect(
      rolesAvailableToAssign(schoolUser, ["180000100"], ["Teacher", "Grader"], roles)
    ).toStrictEqual([]);
  });

  it("no ofrece nada cuando el caller no puede gestionar al objetivo", () => {
    expect(rolesAvailableToAssign(schoolUser, ["999999999"], [], roles)).toStrictEqual([]);
    expect(rolesAvailableToAssign(schoolUserNoCues, [], [], roles)).toStrictEqual([]);
  });
});

describe("canOfferRoleRevoke", () => {
  it("ofrece quitar roles cuando el caller puede gestionar al objetivo", () => {
    expect(canOfferRoleRevoke(admin, [], "Admin")).toBe(true);
    expect(canOfferRoleRevoke(provinceUser, [], "Admin")).toBe(true);
    expect(canOfferRoleRevoke(schoolUser, ["180000100"], "Teacher")).toBe(true);
  });

  it("no ofrece quitar un rol restringido aunque el caller pueda gestionar al objetivo", () => {
    expect(canManageUser(schoolUser, ["180000100"])).toBe(true);
    expect(canOfferRoleRevoke(schoolUser, ["180000100"], "Admin")).toBe(false);
    expect(canOfferRoleRevoke(schoolUser, ["180000100"], "RosterProvince")).toBe(false);
  });

  it("ofrece quitar un rol no restringido con el mismo caller", () => {
    expect(canOfferRoleRevoke(schoolUser, ["180000100"], "Teacher")).toBe(true);
  });

  it("no ofrece quitar roles cuando el caller no puede gestionar al objetivo", () => {
    expect(canOfferRoleRevoke(schoolUser, ["999999999"], "Teacher")).toBe(false);
    expect(canOfferRoleRevoke(schoolUser, [], "Teacher")).toBe(false);
    expect(canOfferRoleRevoke(schoolUserNoCues, [], "Teacher")).toBe(false);
  });
});

describe("canOfferCueRevoke", () => {
  it("ofrece quitar un CUE dentro del alcance del caller", () => {
    expect(canOfferCueRevoke(admin, "999999999")).toBe(true);
    expect(canOfferCueRevoke(schoolUser, "180000100")).toBe(true);
  });

  it("no ofrece quitar un CUE fuera del alcance del caller", () => {
    expect(canOfferCueRevoke(schoolUser, "999999999")).toBe(false);
    expect(canOfferCueRevoke(schoolUserNoCues, "180000100")).toBe(false);
  });
});

describe("cuesAvailableToAssign", () => {
  it("devuelve null cuando el caller tiene alcance ilimitado", () => {
    expect(cuesAvailableToAssign(admin, ["180000100"])).toBeNull();
    expect(cuesAvailableToAssign(provinceUser, [])).toBeNull();
  });

  it("excluye los CUEs propios que el objetivo ya tiene, comparando normalizados", () => {
    const caller: UserProfile = { ...schoolUser, rosterCues: ["180-000-100", "180000200"] };
    expect(cuesAvailableToAssign(caller, ["180000100"])).toStrictEqual(["180000200"]);
    expect(cuesAvailableToAssign(caller, ["180-000-200"])).toStrictEqual(["180000100"]);
    expect(cuesAvailableToAssign(caller, ["180000100", "180-000-200"])).toStrictEqual([]);
  });

  it("excluye el CUE propio cuando el objetivo lo tiene con ruido de guiones", () => {
    const caller: UserProfile = { ...schoolUser, rosterCues: ["180-000-100"] };
    expect(cuesAvailableToAssign(caller, ["180000100"])).toStrictEqual([]);
  });

  it("devuelve un array vacío para un caller de escuela sin CUEs propios", () => {
    expect(cuesAvailableToAssign(schoolUserNoCues, ["999999999"])).toStrictEqual([]);
    expect(cuesAvailableToAssign(schoolUserNoCues, [])).toStrictEqual([]);
  });
});

describe("hasAnyRoleOrCueAction", () => {
  it("es true cuando la única acción es asignar un CUE propio a un objetivo sin CUEs", () => {
    const target = { cues: [], roleCodes: [] };
    expect(canManageUser(schoolUser, target.cues)).toBe(false);
    expect(rolesAvailableToAssign(schoolUser, target.cues, target.roleCodes, roles)).toStrictEqual(
      []
    );
    expect(hasAnyRoleOrCueAction(schoolUser, target, roles)).toBe(true);
  });

  it("es false cuando no hay nada que hacer en absoluto", () => {
    const target = { cues: ["999999999"], roleCodes: [] };
    expect(target.roleCodes).toStrictEqual([]);
    expect(rolesAvailableToAssign(schoolUserNoCues, target.cues, target.roleCodes, roles)).toStrictEqual(
      []
    );
    expect(target.cues.some(cue => canOfferCueRevoke(schoolUserNoCues, cue))).toBe(false);
    expect(cuesAvailableToAssign(schoolUserNoCues, target.cues)).toStrictEqual([]);
    expect(hasAnyRoleOrCueAction(schoolUserNoCues, target, roles)).toBe(false);
  });

  it("la revocacion de un rol Admin no aporta para un caller de escuela", () => {
    const target = { cues: ["180000100"], roleCodes: ["Admin"] };
    const onlyAdmin: Pick<RoleSummary, "code">[] = [{ code: "Admin" }];

    // El gate de revocacion es por codigo de rol: Admin exige scope ilimitado, aunque el
    // caller de escuela pueda gestionar al objetivo. Antes del cambio este camino era true.
    expect(canManageUser(schoolUser, target.cues)).toBe(true);
    expect(canOfferRoleRevoke(schoolUser, target.cues, "Admin")).toBe(false);
    expect(canOfferRoleRevoke(schoolUser, target.cues, "Teacher")).toBe(true);

    // Con la unica accion potencial siendo revocar Admin, el panel no se ofrece: ni la
    // revocacion (Admin exige scope ilimitado), ni la asignacion (el objetivo ya tiene Admin
    // y es el unico rol disponible), ni ningun camino de CUE.
    const soloAdmin = { cues: ["999999999"], roleCodes: ["Admin"] };
    expect(canOfferRoleRevoke(schoolUserNoCues, soloAdmin.cues, "Admin")).toBe(false);
    expect(
      rolesAvailableToAssign(schoolUserNoCues, soloAdmin.cues, soloAdmin.roleCodes, onlyAdmin)
    ).toStrictEqual([]);
    expect(soloAdmin.cues.some(cue => canOfferCueRevoke(schoolUserNoCues, cue))).toBe(false);
    expect(cuesAvailableToAssign(schoolUserNoCues, soloAdmin.cues)).toStrictEqual([]);
    expect(hasAnyRoleOrCueAction(schoolUserNoCues, soloAdmin, onlyAdmin)).toBe(false);
  });
});

describe("roleLabel", () => {
  it("traduce los codigos de rol conocidos al espanol", () => {
    expect(roleLabel("Admin")).toBe("Administrador");
    expect(roleLabel("RosterProvince")).toBe("Padrón provincial");
    expect(roleLabel("RosterSchool")).toBe("Padrón escolar");
    expect(roleLabel("ExamAuthor")).toBe("Autor de exámenes");
    expect(roleLabel("Grader")).toBe("Corrector");
    expect(roleLabel("Operator")).toBe("Operador");
  });

  it("devuelve el codigo sin cambios cuando es desconocido", () => {
    expect(roleLabel("Teacher")).toBe("Teacher");
    expect(roleLabel("")).toBe("");
  });
});
