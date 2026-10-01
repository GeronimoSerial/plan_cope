import { describe, expect, it } from "vitest";
import { getSectionsForGrade, orderSelectedGrades, resolveGradeChange, sortGradeOptions, type GradeSectionOption } from "./grade-section-picker";

const grades: GradeSectionOption[] = [
  { value: "g10", label: "10°", sections: [{ value: "10-c", label: "C" }] },
  { value: "g2", label: "2°", sections: [{ value: "2-b", label: "B" }, { value: "2-a-tarde", label: "A", shift: "Tarde" }, { value: "2-a-manana", label: "A", shift: "Mañana" }] },
  { value: "sala5", label: "Sala de 5" },
  { value: "g1", label: "1°", sections: [{ value: "1-a", label: "A" }] }
];

describe("GradeSectionPicker model", () => {
  it("sorts grade labels naturally and keeps named levels after numbered grades", () => {
    expect(sortGradeOptions(grades).map(grade => grade.value)).toEqual(["g1", "g2", "g10", "sala5"]);
  });

  it("groups multi-select grades by level and stores selections in level-then-grade order", () => {
    const grouped = [
      { value: "secundaria-2", label: "Secundaria 2° año", group: "Secundaria" },
      { value: "primaria-2", label: "Primaria 2° grado", group: "Primaria" },
      { value: "secundaria-1", label: "Secundaria 1° año", group: "Secundaria" },
      { value: "primaria-1", label: "Primaria 1° grado", group: "Primaria" }
    ];

    expect(sortGradeOptions(grouped).map(grade => grade.value)).toEqual([
      "primaria-1", "primaria-2", "secundaria-1", "secundaria-2"
    ]);
    expect(orderSelectedGrades(grouped, ["secundaria-1", "primaria-2", "secundaria-2", "primaria-1"])).toEqual([
      "primaria-1", "primaria-2", "secundaria-1", "secundaria-2"
    ]);
  });

  it("shows only the chosen grade sections, ordered naturally with shift labels for duplicate sections", () => {
    expect(getSectionsForGrade(grades, "g2")).toEqual([
      { value: "2-a-manana", label: "A · Mañana", shift: "Mañana" },
      { value: "2-a-tarde", label: "A · Tarde", shift: "Tarde" },
      { value: "2-b", label: "B", shift: undefined }
    ]);
    expect(getSectionsForGrade(grades, "g10")).toEqual([{ value: "10-c", label: "C", shift: undefined }]);
  });

  it("resets sections on grade changes and auto-selects a grade's only section", () => {
    expect(resolveGradeChange(grades, "g2")).toEqual({ gradeValue: "g2", sectionValue: "" });
    expect(resolveGradeChange(grades, "g1")).toEqual({ gradeValue: "g1", sectionValue: "1-a" });
    expect(resolveGradeChange(grades, "")).toEqual({ gradeValue: "", sectionValue: "" });
  });

  it("clears section selection when the filter is set to all grades", () => {
    expect(resolveGradeChange(grades, "all")).toEqual({ gradeValue: "all", sectionValue: "" });
  });
});
