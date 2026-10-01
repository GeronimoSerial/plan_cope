import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const appRoot = join(dirname(fileURLToPath(import.meta.url)), "..");
const source = (path: string) => readFileSync(join(appRoot, path), "utf8");

describe("copy de páginas", () => {
  it("mantiene descripciones breves y omite las que repiten el título o acciones", () => {
    expect(source("(app)/descargas/page.tsx")).toContain(
      'description="Después de instalar, activá cada equipo con una clave de activación."'
    );
    expect(source("(app)/estadisticas/page.tsx")).toContain(
      'description="Los datos llegan al sincronizar los equipos."'
    );
    expect(source("_components/keys/activation-keys-panel.tsx")).toContain(
      'description="Emití una clave a nombre de una persona y administrá desde acá sus equipos activados."'
    );

    for (const [path, oldCopy] of [
      ["(app)/dashboard/page.tsx", "Resumen de los exámenes."],
      ["(app)/exams/page.tsx", "Exámenes del sistema."],
      ["(app)/usuarios/page.tsx", "Usuarios de Central y sus roles."],
      ["(app)/escuelas/page.tsx", "La lista se arma con el padrón."],
    ]) {
      expect(source(path)).not.toContain(oldCopy);
    }
  });

  it("muestra solo el código bajo el título del examen", () => {
    const detail = source("(app)/exams/[examId]/page.tsx");
    expect(detail).toContain("description={exam.code}");
    expect(detail).not.toContain("Versiones del examen, del borrador");
    expect(detail).not.toContain("<BreadcrumbPage>{exam.title}</BreadcrumbPage>");
  });

  it("oculta la acción de edición cuando no hay una versión editable", () => {
    const actions = source("_components/exams/exam-header-actions.tsx");
    expect(actions).not.toContain("<Button disabled>Editar</Button>");
  });
});
