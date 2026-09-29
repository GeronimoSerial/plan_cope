import { describe, it, expect } from "vitest";
import {
  TARGET_RESULT_LIMIT,
  buildPublishRequest,
  searchNodes,
  searchSchools,
  summarizeTargets,
  validatePublishTargets,
  type NodeTargetRow,
  type SchoolTargetRow
} from "./publish-targets";

describe("buildPublishRequest", () => {
  it("modo all: omite nodeIds y schoolIds", () => {
    const request = buildPublishRequest({ grade: "6", subject: "Matemática", mode: "all" });
    expect(request).toEqual({ grade: "6", subject: "Matemática" });
    expect(request.nodeIds).toBeUndefined();
    expect(request.schoolIds).toBeUndefined();
  });

  it("modo schools: envía los CUE y no nodeIds", () => {
    const request = buildPublishRequest({
      grade: " 6 ",
      mode: "schools",
      schoolIds: ["1001", "1001", "1002"]
    });
    expect(request.grade).toBe("6");
    expect(request.schoolIds).toEqual(["1001", "1002"]);
    expect(request.nodeIds).toBeUndefined();
  });

  it("modo nodes: envía solo nodeIds", () => {
    const request = buildPublishRequest({ grade: "1", mode: "nodes", nodeIds: ["node_a", "node_a", "node_b"] });
    expect(request.nodeIds).toEqual(["node_a", "node_b"]);
    expect(request.schoolIds).toBeUndefined();
  });

  it("omite arrays vacíos", () => {
    const schools = buildPublishRequest({ grade: "6", mode: "schools", schoolIds: [] });
    const nodes = buildPublishRequest({ grade: "6", mode: "nodes", nodeIds: ["", "  "] });
    expect(schools.schoolIds).toBeUndefined();
    expect(nodes.nodeIds).toBeUndefined();
  });

  it("normaliza subject vacío a null", () => {
    const request = buildPublishRequest({ grade: "6", subject: "  ", mode: "all" });
    expect(request.subject).toBeNull();
  });
});

describe("validatePublishTargets", () => {
  it("exige el curso/grado", () => {
    expect(
      validatePublishTargets({ grade: "  ", mode: "all", schoolIds: [], nodeIds: [] })
    ).toEqual({ field: "grade", message: "Ingresá el curso o grado." });
  });

  it("exige selección en modo schools", () => {
    expect(
      validatePublishTargets({ grade: "6", mode: "schools", schoolIds: [], nodeIds: [] })
    ).toEqual({ field: "targets", message: "Elegí al menos una escuela." });
  });

  it("exige selección en modo nodes", () => {
    expect(
      validatePublishTargets({ grade: "6", mode: "nodes", schoolIds: [], nodeIds: [] })
    ).toEqual({ field: "targets", message: "Elegí al menos un nodo." });
  });

  it("acepta una selección válida", () => {
    expect(
      validatePublishTargets({ grade: "6", mode: "schools", schoolIds: ["1001"], nodeIds: [] })
    ).toBeNull();
    expect(validatePublishTargets({ grade: "6", mode: "all", schoolIds: [], nodeIds: [] })).toBeNull();
  });
});

const schools: SchoolTargetRow[] = [
  { id: "s1", cue: "100000001", code: "E1", name: "Escuela Uno", localityId: "l1", status: "Active" },
  { id: "s2", cue: "100000002", code: "E2", name: "Escuela Dos", localityId: "l1", status: "Active" },
  { id: "s3", cue: "200000003", code: "E3", name: "Colegio Norte", localityId: "l2", status: "Inactive" }
];

describe("searchSchools", () => {
  it("busca por CUE o nombre y limita resultados", () => {
    expect(searchSchools(schools, "100000002").rows.map(s => s.cue)).toEqual(["100000002"]);
    expect(searchSchools(schools, "colegio").rows.map(s => s.id)).toEqual(["s3"]);
    expect(searchSchools(schools, "").rows.length).toBe(3);
  });

  it("nunca devuelve más que el límite", () => {
    const many = Array.from({ length: 50 }, (_, index) => ({
      id: `s${index}`,
      cue: `1000000${index}`,
      code: "E",
      name: `Escuela ${index}`,
      localityId: "l",
      status: "Active"
    }));
    const result = searchSchools(many, "", TARGET_RESULT_LIMIT);
    expect(result.rows).toHaveLength(TARGET_RESULT_LIMIT);
    expect(result.total).toBe(50);
    expect(result.shown).toBe(TARGET_RESULT_LIMIT);
    expect(result.truncated).toBe(true);
  });

  it("marca truncado=false cuando entra todo", () => {
    expect(searchSchools(schools, "").truncated).toBe(false);
  });
});

const nodes: NodeTargetRow[] = [
  { id: "n1", nodeCode: "NODO-A", cue: "100000001", deviceName: "Aula 1", schoolName: "Escuela Uno" },
  { id: "n2", nodeCode: "NODO-B", cue: "100000002", deviceName: "Aula 2", schoolName: "Escuela Dos" },
  { id: "n3", nodeCode: "NODO-C", cue: "100000003", deviceName: "Aula 3", schoolName: "Escuela Tres", revokedAt: "2026-01-01T00:00:00Z" }
];

describe("searchNodes", () => {
  it("oculta los nodos revocados", () => {
    expect(searchNodes(nodes, "").rows.map(n => n.id)).toEqual(["n1", "n2"]);
  });

  it("busca por código, CUE, equipo o escuela", () => {
    expect(searchNodes(nodes, "nodo-b").rows.map(n => n.id)).toEqual(["n2"]);
    expect(searchNodes(nodes, "100000002").rows.map(n => n.id)).toEqual(["n2"]);
    expect(searchNodes(nodes, "aula 2").rows.map(n => n.id)).toEqual(["n2"]);
  });

  it("no cuenta los nodos revocados en el total", () => {
    const result = searchNodes(nodes, "");
    expect(result.total).toBe(2);
    expect(result.truncated).toBe(false);
  });
});

describe("summarizeTargets", () => {
  it("resume el modo all", () => {
    expect(summarizeTargets("all", [], [])).toBe("Se entrega a: todas las escuelas");
  });

  it("resume escuelas con plural y CUE", () => {
    expect(summarizeTargets("schools", schools.slice(0, 2), [])).toBe("2 escuelas: CUE 100000001, 100000002");
    expect(summarizeTargets("schools", schools.slice(0, 1), [])).toBe("1 escuela: CUE 100000001");
  });

  it("resume nodos", () => {
    expect(summarizeTargets("nodes", [], nodes.slice(0, 2))).toBe("2 nodos: NODO-A, NODO-B");
  });
});
