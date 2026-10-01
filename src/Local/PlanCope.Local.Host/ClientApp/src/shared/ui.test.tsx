// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { Dialog, SearchableCombobox } from "./ui";

Object.assign(globalThis, { IS_REACT_ACT_ENVIRONMENT: true });

describe("Dialog", () => {
  it("generates a distinct title id for each dialog", () => {
    const html = renderToStaticMarkup(
      <>
        <Dialog title="Primero">Contenido</Dialog>
        <Dialog title="Segundo">Contenido</Dialog>
      </>
    );
    const ids = Array.from(html.matchAll(/aria-labelledby="([^"]+)"/g), match => match[1]);

    expect(ids).toHaveLength(2);
    expect(new Set(ids).size).toBe(2);
    for (const id of ids) expect(html).toContain(`id="${id}"`);
  });
});

describe("SearchableCombobox", () => {
  it("filters accents and CUE digits, supports keyboard selection and Escape", () => {
    const container = document.createElement("div"); document.body.append(container);
    const root: Root = createRoot(container);
    let value = "";
    const render = () => root.render(<SearchableCombobox label="Escuela" id="school" value={value} onChange={next => { value = next; render(); }}
      options={[{ value: "1", label: "Escuela N° 123 \"Dr. Juan Pújol\"", description: "CUE 180055400" }, { value: "2", label: "Escuela Sur", description: "CUE 987654321" }]} />);
    act(render);
    const input = container.querySelector<HTMLInputElement>("input")!;
    act(() => { input.focus(); setInputValue(input, "pujol"); });
    expect(container.querySelectorAll('[role="option"]')).toHaveLength(1);
    for (const query of ["PUJOL", "Pújol", "dr juan", "juan pujol 123", "1800554"]) {
      act(() => setInputValue(input, query));
      expect(container.querySelectorAll('[role="option"]')).toHaveLength(1);
    }
    act(() => setInputValue(input, "notaword"));
    expect(container.querySelectorAll('[role="option"]')).toHaveLength(0);
    expect(container.querySelector('[role="presentation"]')?.textContent).toBe("Sin resultados");
    act(() => setInputValue(input, "Pújol"));
    act(() => input.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })));
    expect(input.getAttribute("aria-activedescendant")).toContain("option-0");
    act(() => input.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
    expect(value).toBe("1");
    act(() => { input.focus(); input.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })); });
    expect(input.getAttribute("aria-expanded")).toBe("false");
    act(() => input.click());
    expect(input.getAttribute("aria-expanded")).toBe("true");
    act(() => root.unmount()); container.remove();
  });

  it("lets keyboard navigation reach options beyond the first eight", () => {
    const container = document.createElement("div"); document.body.append(container);
    const root: Root = createRoot(container);
    const options = Array.from({ length: 12 }, (_, index) => ({ value: String(index), label: `Escuela ${index + 1}`, description: `CUE ${index + 1}` }));
    act(() => root.render(<SearchableCombobox label="Escuela" id="many-schools" value="" onChange={() => undefined} options={options} />));
    const input = container.querySelector<HTMLInputElement>("input")!;
    act(() => input.focus());
    expect(container.querySelectorAll('[role="option"]')).toHaveLength(12);
    act(() => { for (let index = 0; index < 12; index++) input.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })); });
    expect(input.getAttribute("aria-activedescendant")).toBe("many-schools-listbox-option-11");
    expect(container.querySelector("#many-schools-listbox-option-11")?.textContent).toContain("Escuela 12");
    act(() => root.unmount()); container.remove();
  });
});

function setInputValue(input: HTMLInputElement, value: string) {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(input, value);
  input.dispatchEvent(new Event("input", { bubbles: true }));
}
