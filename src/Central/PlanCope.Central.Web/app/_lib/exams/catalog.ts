export const courseOptions = [
  { key: "primaria-1", label: "Primaria 1° grado", level: "Primaria" },
  { key: "primaria-2", label: "Primaria 2° grado", level: "Primaria" },
  { key: "primaria-3", label: "Primaria 3° grado", level: "Primaria" },
  { key: "primaria-4", label: "Primaria 4° grado", level: "Primaria" },
  { key: "primaria-5", label: "Primaria 5° grado", level: "Primaria" },
  { key: "primaria-6", label: "Primaria 6° grado", level: "Primaria" },
  { key: "secundaria-1", label: "Secundaria 1° año", level: "Secundaria" },
  { key: "secundaria-2", label: "Secundaria 2° año", level: "Secundaria" },
  { key: "secundaria-3", label: "Secundaria 3° año", level: "Secundaria" },
  { key: "secundaria-4", label: "Secundaria 4° año", level: "Secundaria" },
  { key: "secundaria-5", label: "Secundaria 5° año", level: "Secundaria" },
  { key: "secundaria-6", label: "Secundaria 6° año", level: "Secundaria" }
] as const;

export const areaOptions = [
  "Matemática", "Lengua", "Ciencias Naturales", "Ciencias Sociales", "Inglés",
  "Educación Física", "Educación Artística", "Tecnología", "Formación Ética y Ciudadana", "Otro"
] as const;

export function courseLabel(key: string): string {
  return courseOptions.find(course => course.key === key)?.label ?? key;
}
