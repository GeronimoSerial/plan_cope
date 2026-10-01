"use client";

import { useEffect, useMemo, useState } from "react";
import { Checkbox } from "@/components/ui/checkbox";
import { Field, FieldLabel } from "@/components/ui/field";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

export interface GradeSectionOption {
  value: string;
  label: string;
  group?: string;
  sections?: readonly SectionOption[];
}

export interface SectionOption {
  value: string;
  label: string;
  shift?: string | null;
}

interface GradeSectionPickerProps {
  grades: readonly GradeSectionOption[];
  mode?: "single" | "multi";
  value: string | readonly string[];
  onValueChange: (value: string | string[]) => void;
  sectionValue?: string;
  onSectionValueChange?: (value: string) => void;
  sectionValues?: Readonly<Record<string, string>>;
  onSectionValuesChange?: (values: Record<string, string>) => void;
  includeAll?: boolean;
  includeAllSections?: boolean;
  forceSection?: boolean;
  disabled?: boolean;
  showSection?: boolean;
  className?: string;
}

function naturalCompare(left: string, right: string): number {
  return left.localeCompare(right, "es", { numeric: true, sensitivity: "base" });
}

export function sortGradeOptions<T extends { label: string }>(options: readonly T[]): T[] {
  return [...options].sort((left, right) => {
    const leftGroup = "group" in left && typeof left.group === "string" ? left.group : "";
    const rightGroup = "group" in right && typeof right.group === "string" ? right.group : "";
    const groupOrder = naturalCompare(leftGroup, rightGroup);
    if (groupOrder !== 0) return groupOrder;
    const gradeNumber = (label: string) => /^sala\b/i.test(label) ? undefined : label.match(/\d+/)?.[0];
    const leftNumber = gradeNumber(left.label);
    const rightNumber = gradeNumber(right.label);
    if (leftNumber && rightNumber && Number(leftNumber) !== Number(rightNumber)) {
      return Number(leftNumber) - Number(rightNumber);
    }
    if (leftNumber && !rightNumber) return -1;
    if (!leftNumber && rightNumber) return 1;
    return naturalCompare(left.label, right.label);
  });
}

export function orderSelectedGrades(grades: readonly GradeSectionOption[], values: readonly string[]): string[] {
  const selectedGrades = new Set(values);
  return sortGradeOptions(grades.filter(grade => selectedGrades.has(grade.value))).map(grade => grade.value);
}

export function getSectionsForGrade(grades: readonly GradeSectionOption[], gradeValue: string): SectionOption[] {
  const sections = grades.find(grade => grade.value === gradeValue)?.sections ?? [];
  const duplicateLetters = new Set(
    sections.filter(section => section.shift).filter((section, index, all) =>
      all.some((other, otherIndex) => otherIndex !== index && other.label === section.label && other.shift !== section.shift)
    ).map(section => section.label)
  );
  return [...sections].sort((left, right) => naturalCompare(left.label, right.label) || naturalCompare(left.shift ?? "", right.shift ?? ""))
    .map(section => ({
      ...section,
      label: duplicateLetters.has(section.label) && section.shift ? `${section.label} · ${section.shift}` : section.label
    }));
}

export function resolveGradeChange(grades: readonly GradeSectionOption[], gradeValue: string): { gradeValue: string; sectionValue: string } {
  if (!gradeValue || gradeValue === "all") return { gradeValue, sectionValue: "" };
  const sections = getSectionsForGrade(grades, gradeValue);
  return { gradeValue, sectionValue: sections.length === 1 ? sections[0].value : "" };
}

export function GradeSectionPicker({
  grades,
  mode = "single",
  value,
  onValueChange,
  sectionValue = "",
  onSectionValueChange,
  sectionValues,
  onSectionValuesChange,
  includeAll = false,
  includeAllSections = includeAll,
  forceSection = false,
  disabled = false,
  showSection = true,
  className
}: GradeSectionPickerProps) {
  const sortedGrades = useMemo(() => sortGradeOptions(grades), [grades]);
  const selectedGrade = typeof value === "string" ? value : "";
  const sections = useMemo(() => getSectionsForGrade(sortedGrades, selectedGrade), [sortedGrades, selectedGrade]);
  const hasSections = showSection && (forceSection || grades.some(grade => (grade.sections?.length ?? 0) > 0));
  const [selectedByGrade, setSelectedByGrade] = useState<Record<string, string>>({});

  useEffect(() => {
    if (includeAllSections && sectionValue === "all") return;
    if (mode === "single" && sectionValue && !sections.some(section => section.value === sectionValue)) {
      onSectionValueChange?.(sections.length === 1 ? sections[0].value : "");
      return;
    }
    if (mode === "single" && selectedGrade && selectedGrade !== "all" && !sectionValue && sections.length === 1) {
      onSectionValueChange?.(sections[0].value);
    }
  }, [includeAllSections, mode, onSectionValueChange, sectionValue, sections, selectedGrade]);

  function chooseGrade(nextGrade: string) {
    const selection = resolveGradeChange(sortedGrades, nextGrade);
    onValueChange(selection.gradeValue);
    if (hasSections) {
      onSectionValueChange?.(selection.sectionValue);
    }
  }

  if (mode === "multi") {
    const selected = Array.isArray(value) ? value : [];
    const groups = new Map<string, GradeSectionOption[]>();
    for (const grade of sortedGrades) {
      const group = grade.group ?? "";
      groups.set(group, [...(groups.get(group) ?? []), grade]);
    }
    return (
      <div className={className}>
        <fieldset disabled={disabled} className="grid gap-2">
          <legend className="mb-2 text-sm font-medium">Grado</legend>
          <div className="grid grid-cols-2 gap-x-4 gap-y-3 sm:grid-cols-3">
            {[...groups.entries()].map(([group, groupedGrades]) => (
              <fieldset key={group || "grades"} className="grid content-start gap-2">
                {group && <legend className="mb-1 text-sm font-medium">{group}</legend>}
                {groupedGrades.map(grade => (
                  <label key={grade.value} className="flex items-center gap-2 text-sm">
                    <Checkbox checked={selected.includes(grade.value)} disabled={disabled} onCheckedChange={checked => {
                      const next = checked ? [...selected, grade.value] : selected.filter(item => item !== grade.value);
                      onValueChange(orderSelectedGrades(sortedGrades, next));
                      if (checked) {
                        const section = resolveGradeChange(sortedGrades, grade.value).sectionValue;
                        if (section) {
                          setSelectedByGrade(previous => ({ ...previous, [grade.value]: section }));
                          onSectionValuesChange?.({ ...(sectionValues ?? selectedByGrade), [grade.value]: section });
                        }
                      } else {
                        setSelectedByGrade(previous => {
                          const nextSections = { ...previous };
                          delete nextSections[grade.value];
                          return nextSections;
                        });
                        if (sectionValues) {
                          const nextSections = { ...sectionValues };
                          delete nextSections[grade.value];
                          onSectionValuesChange?.(nextSections);
                        }
                      }
                    }} />
                    {grade.label}
                  </label>
                ))}
              </fieldset>
            ))}
          </div>
        </fieldset>
        {hasSections && selected.map(gradeValue => {
          const grade = sortedGrades.find(item => item.value === gradeValue)!;
          const sectionItems = getSectionsForGrade(sortedGrades, gradeValue);
          const selectedSection = sectionValues?.[gradeValue] ?? selectedByGrade[gradeValue] ?? "";
          return (
            <Field key={gradeValue} className="mt-3">
              <FieldLabel htmlFor={`section-${gradeValue}`}>Sección · {grade.label}</FieldLabel>
              <Select value={selectedSection} onValueChange={next => {
                const nextValue = next ?? "";
                setSelectedByGrade(previous => ({ ...previous, [gradeValue]: nextValue }));
                onSectionValuesChange?.({ ...(sectionValues ?? selectedByGrade), [gradeValue]: nextValue });
              }} disabled={disabled}>
                <SelectTrigger id={`section-${gradeValue}`} className="w-full"><SelectValue placeholder={`Elegí una sección de ${grade.label}`} /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">Todas las secciones de {grade.label}</SelectItem>
                  {sectionItems.map(section => <SelectItem key={section.value} value={section.value}>{section.label}</SelectItem>)}
                </SelectContent>
              </Select>
            </Field>
          );
        })}
      </div>
    );
  }

  return (
    <div className={`grid gap-3 sm:grid-cols-2 ${className ?? ""}`}>
      <Field>
        <FieldLabel htmlFor="grade-picker">Grado</FieldLabel>
        <Select value={selectedGrade} onValueChange={next => chooseGrade(next ?? "")} disabled={disabled}>
          <SelectTrigger id="grade-picker" className="w-full"><SelectValue placeholder={includeAll ? "Todos los grados" : "Elegí un grado"} /></SelectTrigger>
          <SelectContent>
            {includeAll && <SelectItem value="all">Todos los grados</SelectItem>}
            {sortedGrades.map(grade => <SelectItem key={grade.value} value={grade.value}>{grade.label}</SelectItem>)}
          </SelectContent>
        </Select>
      </Field>
      {hasSections && (
        <Field>
          <FieldLabel htmlFor="section-picker">Sección</FieldLabel>
          <Select value={sectionValue || ""} onValueChange={next => onSectionValueChange?.(next ?? "")} disabled={disabled || (!selectedGrade && !includeAllSections)}>
            <SelectTrigger id="section-picker" className="w-full">
              <SelectValue placeholder={selectedGrade ? "Elegí una sección" : "Elegí primero un grado"} />
            </SelectTrigger>
            <SelectContent>
              {includeAllSections && <SelectItem value="all">
                {selectedGrade && selectedGrade !== "all" ? `Todas las secciones de ${sortedGrades.find(grade => grade.value === selectedGrade)?.label}` : "Todas las secciones"}
              </SelectItem>}
              {sections.map(section => <SelectItem key={section.value} value={section.value}>{section.label}</SelectItem>)}
            </SelectContent>
          </Select>
        </Field>
      )}
    </div>
  );
}
