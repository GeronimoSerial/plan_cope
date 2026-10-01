// @vitest-environment jsdom

import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render } from "@testing-library/react";
import { GradeSectionPicker } from "./grade-section-picker";

afterEach(cleanup);

describe("GradeSectionPicker multi-level groups", () => {
  it("renders separate level groups with grades ordered within each level", () => {
    const { container } = render(
      <GradeSectionPicker
        mode="multi"
        grades={[
          { value: "secundaria-2", label: "Secundaria 2° año", group: "Secundaria" },
          { value: "primaria-2", label: "Primaria 2° grado", group: "Primaria" },
          { value: "secundaria-1", label: "Secundaria 1° año", group: "Secundaria" },
          { value: "primaria-1", label: "Primaria 1° grado", group: "Primaria" }
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
      "Primaria 1° grado", "Primaria 2° grado", "Secundaria 1° año", "Secundaria 2° año"
    ]);
  });
});
