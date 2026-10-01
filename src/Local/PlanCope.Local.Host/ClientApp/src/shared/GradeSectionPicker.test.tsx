// @vitest-environment jsdom
import { act, useState } from "react";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { GradeSectionPicker, type GradeSectionOption } from "./GradeSectionPicker";

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });

const sections: GradeSectionOption[] = [
  { course: "Sala de 4", division: "A" }, { course: "10°", division: "A" },
  { course: "2°", division: "B" }, { course: "2°", division: "A", shift: "Tarde" },
  { course: "2°", division: "A", shift: "Mañana" }
];

describe("GradeSectionPicker", () => {
  let root: ReturnType<typeof createRoot> | undefined;
  afterEach(() => { if (root) act(() => root?.unmount()); root = undefined; document.body.replaceChildren(); });

  it("naturally sorts grades and sections, resets the section, and selects a single section", () => {
    const element = document.createElement("div"); document.body.append(element); root = createRoot(element);
    function Harness() {
      const [grade, setGrade] = useState(""); const [section, setSection] = useState("");
      return <GradeSectionPicker sections={sections} grade={grade} section={section} onGradeChange={setGrade} onSectionChange={setSection} />;
    }
    act(() => root?.render(<Harness />));
    const selects = element.querySelectorAll("select");
    expect([...selects[0].options].map(option => option.text)).toEqual(["Elegí un grado", "2°", "10°", "Sala de 4"]);
    act(() => { selects[0].value = "2°"; selects[0].dispatchEvent(new Event("change", { bubbles: true })); });
    expect([...selects[1].options].map(option => option.text)).toEqual(["Todas las secciones", "A · Mañana", "A · Tarde", "B"]);
    act(() => { selects[0].value = "10°"; selects[0].dispatchEvent(new Event("change", { bubbles: true })); });
    expect(selects[1].value).toBe("A");
  });

  it("offers the all grades and sections choices in filter mode", () => {
    const element = document.createElement("div"); document.body.append(element); root = createRoot(element);
    act(() => root?.render(<GradeSectionPicker sections={sections} grade="" section="" onGradeChange={() => undefined} onSectionChange={() => undefined} filters />));
    expect(element.querySelector<HTMLOptionElement>('select[aria-label="Grado"] option')?.text).toBe("Todos los grados");
    expect(element.querySelector<HTMLOptionElement>('select[aria-label="Sección"] option')?.text).toBe("Todas las secciones");
    expect(element.querySelector<HTMLSelectElement>('select[aria-label="Sección"]')?.disabled).toBe(true);
  });
});
