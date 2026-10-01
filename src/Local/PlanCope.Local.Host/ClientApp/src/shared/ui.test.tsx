import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { Dialog } from "./ui";

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
