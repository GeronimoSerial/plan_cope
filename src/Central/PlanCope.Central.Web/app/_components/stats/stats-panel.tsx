"use client";

import { useState, type KeyboardEvent, type ReactNode } from "react";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { SchoolStatsRow } from "../../_lib/api/server";

interface CourseStatsRow {
  course: string;
  attemptCount: number | string;
  averageScorePercent: number | string;
}

interface BlockStatsRow {
  blockId: string;
  correctCount: number;
  partialCount: number;
  incorrectCount: number;
  blankCount: number;
  ungradableCount: number;
}

interface ExamStatsRow {
  examVersionId: string;
  examCode: string;
  versionNumber: number | string;
  attemptCount: number | string;
  averageScorePercent: number | string;
  blocks: BlockStatsRow[];
}

interface StatsPanelProps {
  initialSchools: SchoolStatsRow[];
}

// El backend puede responder el string "cohorte insuficiente" en lugar de un número cuando
// la cohorte está suprimida (menos de 5 intentos, vista provincial). Se renderiza tal cual.
function formatPercent(value: number | string): string {
  return typeof value === "number" ? `${value.toFixed(1)}%` : value;
}

function formatCount(value: number | string): string {
  return typeof value === "number" ? String(value) : value;
}

function csvEscape(value: number | string): string {
  const text = typeof value === "number" ? String(value) : value;
  return /[",\n\r]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function buildSchoolsCsv(rows: SchoolStatsRow[]): string {
  const lines = ["cue,attemptCount,averageScorePercent"];
  for (const row of rows) {
    lines.push([csvEscape(row.cue), csvEscape(row.attemptCount), csvEscape(row.averageScorePercent)].join(","));
  }
  return lines.join("\r\n");
}

function activateOnKey(handler: () => void) {
  return (event: KeyboardEvent<HTMLTableRowElement>) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      handler();
    }
  };
}

function TableShell({ children }: { children: ReactNode }) {
  return <div className="overflow-hidden rounded-lg ring-1 ring-foreground/10">{children}</div>;
}

export function StatsPanel({ initialSchools }: StatsPanelProps) {
  const [schools, setSchools] = useState<SchoolStatsRow[]>(initialSchools);
  const [schoolYearInput, setSchoolYearInput] = useState("");
  const [courseInput, setCourseInput] = useState("");
  const [appliedSchoolYear, setAppliedSchoolYear] = useState("");
  const [applying, setApplying] = useState(false);
  const [schoolsError, setSchoolsError] = useState<string | null>(null);

  const [selectedCue, setSelectedCue] = useState<string | null>(null);
  const [courses, setCourses] = useState<CourseStatsRow[]>([]);
  const [coursesLoading, setCoursesLoading] = useState(false);
  const [coursesError, setCoursesError] = useState<string | null>(null);

  const [selectedCourse, setSelectedCourse] = useState<string | null>(null);
  const [exams, setExams] = useState<ExamStatsRow[]>([]);
  const [examsLoading, setExamsLoading] = useState(false);
  const [examsError, setExamsError] = useState<string | null>(null);

  function resetSelection() {
    setSelectedCue(null);
    setCourses([]);
    setCoursesError(null);
    setSelectedCourse(null);
    setExams([]);
    setExamsError(null);
  }

  async function applyFilters() {
    setApplying(true);
    setSchoolsError(null);
    try {
      const params = new URLSearchParams();
      if (schoolYearInput.trim()) params.set("schoolYear", schoolYearInput.trim());
      if (courseInput.trim()) params.set("course", courseInput.trim());
      const query = params.toString();
      const updated = await callCentral<SchoolStatsRow[]>(`stats/schools${query ? `?${query}` : ""}`);
      setSchools(updated);
      setAppliedSchoolYear(schoolYearInput.trim());
      resetSelection();
    } catch (error) {
      setSchoolsError(getErrorMessage(error, "No se pudieron cargar las estadísticas."));
    } finally {
      setApplying(false);
    }
  }

  async function selectCue(cue: string) {
    if (selectedCue === cue) {
      resetSelection();
      return;
    }
    setSelectedCue(cue);
    setCourses([]);
    setCoursesError(null);
    setSelectedCourse(null);
    setExams([]);
    setExamsError(null);
    setCoursesLoading(true);
    try {
      const params = new URLSearchParams({ cue });
      if (appliedSchoolYear) params.set("schoolYear", appliedSchoolYear);
      const updated = await callCentral<CourseStatsRow[]>(`stats/course?${params.toString()}`);
      setCourses(updated);
    } catch (error) {
      setCoursesError(getErrorMessage(error, "No se pudo cargar el detalle por curso."));
    } finally {
      setCoursesLoading(false);
    }
  }

  async function selectCourse(course: string) {
    if (!selectedCue) {
      return;
    }
    if (selectedCourse === course) {
      setSelectedCourse(null);
      setExams([]);
      setExamsError(null);
      return;
    }
    setSelectedCourse(course);
    setExams([]);
    setExamsError(null);
    setExamsLoading(true);
    try {
      const params = new URLSearchParams({ cue: selectedCue, course });
      if (appliedSchoolYear) params.set("schoolYear", appliedSchoolYear);
      const updated = await callCentral<ExamStatsRow[]>(`stats/exam?${params.toString()}`);
      setExams(updated);
    } catch (error) {
      setExamsError(getErrorMessage(error, "No se pudo cargar el detalle por examen."));
    } finally {
      setExamsLoading(false);
    }
  }

  function exportCsv() {
    const blob = new Blob([buildSchoolsCsv(schools)], { type: "text/csv;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const anchor = window.document.createElement("a");
    anchor.href = url;
    anchor.download = "estadisticas-establecimientos.csv";
    window.document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  }

  return (
    <div className="grid gap-6">
      <Card>
        <CardHeader>
          <CardTitle>Establecimientos</CardTitle>
          {schools.length > 0 && (
            <CardAction>
              <Button variant="outline" onClick={exportCsv}>
                Exportar CSV
              </Button>
            </CardAction>
          )}
        </CardHeader>
        <CardContent className="grid gap-4">
          <div className="flex flex-wrap items-end gap-3">
            <div className="grid gap-1.5">
              <Label htmlFor="stats-school-year">Año lectivo (opcional)</Label>
              <Input
                id="stats-school-year"
                value={schoolYearInput}
                onChange={event => setSchoolYearInput(event.target.value)}
                placeholder="Ej. 2025"
                className="w-40"
              />
            </div>
            <div className="grid gap-1.5">
              <Label htmlFor="stats-course">Curso (opcional)</Label>
              <Input
                id="stats-course"
                value={courseInput}
                onChange={event => setCourseInput(event.target.value)}
                placeholder="Ej. 4º A"
                className="w-40"
              />
            </div>
            <Button onClick={() => void applyFilters()} disabled={applying}>
              {applying ? "Aplicando…" : "Aplicar filtros"}
            </Button>
          </div>

          {schoolsError && (
            <Alert variant="destructive">
              <AlertDescription>{schoolsError}</AlertDescription>
            </Alert>
          )}

          {schools.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No hay establecimientos con datos para los filtros seleccionados.
            </p>
          ) : (
            <TableShell>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>CUE</TableHead>
                    <TableHead>Intentos</TableHead>
                    <TableHead>Promedio</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {schools.map(row => (
                    <TableRow
                      key={row.cue}
                      role="button"
                      tabIndex={0}
                      aria-expanded={selectedCue === row.cue}
                      className="cursor-pointer"
                      onClick={() => void selectCue(row.cue)}
                      onKeyDown={activateOnKey(() => void selectCue(row.cue))}
                    >
                      <TableCell className="font-mono font-medium">{row.cue}</TableCell>
                      <TableCell>{formatCount(row.attemptCount)}</TableCell>
                      <TableCell>{formatPercent(row.averageScorePercent)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableShell>
          )}
        </CardContent>
      </Card>

      {selectedCue && (
        <Card>
          <CardHeader>
            <CardTitle>Detalle del CUE {selectedCue}</CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4">
            {coursesLoading && <p className="text-sm text-muted-foreground">Cargando cursos…</p>}
            {coursesError && (
              <Alert variant="destructive">
                <AlertDescription>{coursesError}</AlertDescription>
              </Alert>
            )}
            {!coursesLoading && !coursesError && courses.length === 0 && (
              <p className="text-sm text-muted-foreground">Sin datos por curso para este establecimiento.</p>
            )}

            {courses.length > 0 && (
              <TableShell>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Curso</TableHead>
                      <TableHead>Intentos</TableHead>
                      <TableHead>Promedio</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {courses.map(course => (
                      <TableRow
                        key={course.course}
                        role="button"
                        tabIndex={0}
                        aria-expanded={selectedCourse === course.course}
                        className="cursor-pointer"
                        onClick={() => void selectCourse(course.course)}
                        onKeyDown={activateOnKey(() => void selectCourse(course.course))}
                      >
                        <TableCell className="font-medium">{course.course}</TableCell>
                        <TableCell>{formatCount(course.attemptCount)}</TableCell>
                        <TableCell>{formatPercent(course.averageScorePercent)}</TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableShell>
            )}

            {selectedCourse && (
              <div className="grid gap-4">
                {examsLoading && <p className="text-sm text-muted-foreground">Cargando exámenes…</p>}
                {examsError && (
                  <Alert variant="destructive">
                    <AlertDescription>{examsError}</AlertDescription>
                  </Alert>
                )}
                {!examsLoading && !examsError && exams.length === 0 && (
                  <p className="text-sm text-muted-foreground">Sin exámenes rendidos para este curso.</p>
                )}

                {exams.map(exam => (
                  <div key={exam.examVersionId} className="grid gap-2">
                    <h3 className="text-sm font-medium">
                      {exam.examCode} · versión {exam.versionNumber}
                    </h3>
                    <p className="text-sm text-muted-foreground">
                      Intentos: {formatCount(exam.attemptCount)} · Promedio:{" "}
                      {formatPercent(exam.averageScorePercent)}
                    </p>
                    {exam.blocks.length === 0 ? (
                      <p className="text-sm text-muted-foreground">Sin datos por bloque.</p>
                    ) : (
                      <TableShell>
                        <Table>
                          <TableHeader>
                            <TableRow>
                              <TableHead>Bloque</TableHead>
                              <TableHead>Correctas</TableHead>
                              <TableHead>Parciales</TableHead>
                              <TableHead>Incorrectas</TableHead>
                              <TableHead>En blanco</TableHead>
                              <TableHead>No calificables</TableHead>
                            </TableRow>
                          </TableHeader>
                          <TableBody>
                            {exam.blocks.map(block => (
                              <TableRow key={block.blockId}>
                                <TableCell className="font-mono">{block.blockId}</TableCell>
                                <TableCell>{block.correctCount}</TableCell>
                                <TableCell>{block.partialCount}</TableCell>
                                <TableCell>{block.incorrectCount}</TableCell>
                                <TableCell>{block.blankCount}</TableCell>
                                <TableCell>{block.ungradableCount}</TableCell>
                              </TableRow>
                            ))}
                          </TableBody>
                        </Table>
                      </TableShell>
                    )}
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </div>
  );
}
