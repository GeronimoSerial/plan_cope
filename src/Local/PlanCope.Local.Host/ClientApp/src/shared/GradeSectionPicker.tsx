import { useEffect, useMemo } from "react";

export type GradeSectionOption = { course: string; division: string; shift?: string | null; value?: string };

export function sectionOptionValue(item: GradeSectionOption): string {
  return item.value ?? (item.shift ? `${item.division}\u001f${item.shift}` : item.division);
}

type Props = {
  sections: GradeSectionOption[];
  grades?: string[];
  grade: string;
  section: string;
  onGradeChange: (value: string) => void;
  onSectionChange: (value: string) => void;
  filters?: boolean;
  disabled?: boolean;
  gradeId?: string;
  sectionId?: string;
};

const natural = new Intl.Collator("es", { numeric: true, sensitivity: "base" });

export function GradeSectionPicker({ sections, grades: additionalGrades = [], grade, section, onGradeChange, onSectionChange, filters = false, disabled = false, gradeId = "grade-filter", sectionId = "section-filter" }: Props) {
  const grades = useMemo(() => [...new Set([...sections.map(item => item.course.trim()), ...additionalGrades.map(item => item.trim())].filter(Boolean))].sort(sortGrades), [sections, additionalGrades]);
  const options = useMemo(() => sections.filter(item => item.course === grade)
    .sort((a, b) => natural.compare(a.division, b.division) || natural.compare(a.shift ?? "", b.shift ?? "")), [sections, grade]);
  useEffect(() => {
    if (grade && options.length === 1 && !section) onSectionChange(sectionOptionValue(options[0]));
  }, [grade, options, section, onSectionChange]);
  const byDivisionCount = options.reduce<Record<string, number>>((result, item) => ({ ...result, [item.division]: (result[item.division] ?? 0) + 1 }), {});
  return <>
    <label htmlFor={gradeId}>Grado<select className="control" id={gradeId} aria-label="Grado" value={grade} disabled={disabled} onChange={event => { onGradeChange(event.target.value); onSectionChange(""); }}>
      <option value="">{filters ? "Todos los grados" : "Elegí un grado"}</option>
      {grades.map(value => <option key={value} value={value}>{formatGrade(value)}</option>)}
    </select></label>
    <label htmlFor={sectionId}>Sección<select className="control" id={sectionId} aria-label="Sección" value={section} disabled={disabled || !grade} onChange={event => onSectionChange(event.target.value)}>
      <option value="">{grade || filters ? "Todas las secciones" : "Elegí primero un grado"}</option>
      {options.map(item => <option key={sectionOptionValue(item)} value={sectionOptionValue(item)}>
        {item.division}{byDivisionCount[item.division] > 1 && item.shift ? ` · ${item.shift}` : ""}
      </option>)}
    </select></label>
  </>;
}

function formatGrade(value: string): string {
  return value.replace(/º/g, "°");
}

function sortGrades(a: string, b: string): number {
  const numberA = a.match(/^\s*(\d+)/)?.[1];
  const numberB = b.match(/^\s*(\d+)/)?.[1];
  if (numberA && numberB) return Number(numberA) - Number(numberB) || natural.compare(a, b);
  if (numberA) return -1;
  if (numberB) return 1;
  return natural.compare(a, b);
}
