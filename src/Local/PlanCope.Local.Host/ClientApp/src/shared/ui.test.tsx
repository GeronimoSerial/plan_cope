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
      options={[{ value: "1", label: "Escuela Álamo", description: "CUE 123456789" }, { value: "2", label: "Escuela Sur", description: "CUE 987654321" }]} />);
    act(render);
    const input = container.querySelector<HTMLInputElement>("input")!;
    act(() => { input.focus(); setInputValue(input, "alamo"); });
    expect(container.querySelectorAll('[role="option"]')).toHaveLength(1);
    act(() => setInputValue(input, "123456"));
    expect(container.querySelectorAll('[role="option"]')).toHaveLength(1);
    act(() => input.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true })));
    expect(input.getAttribute("aria-activedescendant")).toContain("option-0");
    act(() => input.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })));
    expect(value).toBe("1");
    act(() => { input.focus(); input.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })); });
    expect(input.getAttribute("aria-expanded")).toBe("false");
    act(() => root.unmount()); container.remove();
  });
});

function setInputValue(input: HTMLInputElement, value: string) {
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(input, value);
  input.dispatchEvent(new Event("input", { bubbles: true }));
}
