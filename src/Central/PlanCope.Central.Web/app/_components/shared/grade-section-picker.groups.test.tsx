// @vitest-environment jsdom

import { afterEach, describe, expect, it } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { GradeSectionPicker } from "./grade-section-picker";

afterEach(cleanup);

describe("GradeSectionPicker multi-level groups", () => {
  it("renders separate level groups with grades ordered within each level", () => {
    const { container } = render(
      <GradeSectionPicker
        mode="multi"
        grades={[
          { value: "secundaria-2", label: "Secundaria · 2° año", group: "Secundaria" },
          { value: "primaria-2", label: "Primaria · 2° grado", group: "Primaria" },
          { value: "secundaria-1", label: "Secundaria · 1° año", group: "Secundaria" },
          { value: "primaria-1", label: "Primaria · 1° grado", group: "Primaria" }
        ]}
        value={[]}
        onValueChange={() => undefined}
        showSection={false}
      />
    );

    expect(Array.from(container.querySelectorAll("fieldset > legend")).map(legend => legend.textContent)).toEqual([
      "Grado", "Primaria", "Secundaria"
    ]);
    expect(Array.from(container.querySelectorAll("label")).map(label => label.textContent)).toEqual([
      "Primaria · 1° grado", "Primaria · 2° grado", "Secundaria · 1° año", "Secundaria · 2° año"
    ]);
    const groupGrid = container.querySelector(".grid-cols-1");
    expect(groupGrid?.classList.contains("sm:grid-cols-[minmax(14rem,1fr)_minmax(14rem,1fr)]")).toBe(true);
    expect(Array.from(container.querySelectorAll("label")).every(label => label.classList.contains("min-h-11"))).toBe(true);
  });
});

describe("GradeSectionPicker catalog labels", () => {
  it("uses the catalog label for raw grade keys in the option and closed trigger", async () => {
    const { container } = render(
      <GradeSectionPicker
        grades={[{ value: "secundaria-1", label: "secundaria-1" }]}
        value="secundaria-1"
        onValueChange={() => undefined}
        showSection={false}
      />
    );

    const trigger = container.querySelector("#grade-picker");
    expect(trigger?.textContent).toContain("Secundaria · 1° año");
    expect(trigger?.textContent).not.toContain("secundaria-1");
    fireEvent.click(trigger!);
    expect(await screen.findByRole("option", { name: "Secundaria · 1° año" })).toBeTruthy();
  });
});
