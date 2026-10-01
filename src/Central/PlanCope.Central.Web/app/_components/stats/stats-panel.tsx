"use client";

import { useEffect, useState, type KeyboardEvent, type ReactNode } from "react";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardAction, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { TermLabel } from "../help/term-hint";
import type { SchoolStatsRow } from "../../_lib/api/server";
import { GradeSectionPicker } from "../shared/grade-section-picker";

interface CourseStatsRow {
  course: string;
  attemptCount: number | string;
  averageScorePercent: number | string;
}

interface GradeFilterOption {
  value: string;
  label: string;
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

// Muestra un número con su formato o, si el backend suprimió el dato, el texto con su explicación.
function StatValue({ value, format }: { value: number | string; format: (value: number | string) => string }) {
  if (typeof value === "string") {
    return <TermLabel term="stats-cohorte-insuficiente">{value}</TermLabel>;
  }
  return <>{format(value)}</>;
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
  return <div className="overflow-x-auto rounded-lg ring-1 ring-foreground/10">{children}</div>;
}

export function StatsPanel({ initialSchools }: StatsPanelProps) {
  const [schools, setSchools] = useState<SchoolStatsRow[]>(initialSchools);
  const [schoolYearInput, setSchoolYearInput] = useState("");
  const [courseInput, setCourseInput] = useState("");
  const [gradeOptions, setGradeOptions] = useState<GradeFilterOption[]>([]);
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

  useEffect(() => {
    let active = true;
    void callCentral<GradeFilterOption[]>("stats/courses")
      .then(options => { if (active) setGradeOptions(options); })
      .catch(() => { if (active) setGradeOptions([]); });
    return () => { active = false; };
  }, []);

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
              <GradeSectionPicker
                grades={gradeOptions}
                mode="single"
                value={courseInput || "all"}
                onValueChange={next => setCourseInput(typeof next === "string" && next !== "all" ? next : "")}
                includeAll
                showSection={false}
                className="min-w-44"
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
                    <TableHead>
                      <TermLabel term="cue">CUE</TermLabel>
                    </TableHead>
                    <TableHead>
                      <TermLabel term="stats-intentos">Intentos</TermLabel>
                    </TableHead>
                    <TableHead>
                      <TermLabel term="stats-promedio">Promedio de puntaje (%)</TermLabel>
                    </TableHead>
                    <TableHead>Sesiones en curso</TableHead>
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
                      <TableCell>
                        <StatValue value={row.attemptCount} format={formatCount} />
                      </TableCell>
                      <TableCell>
                        <StatValue value={row.averageScorePercent} format={formatPercent} />
                      </TableCell>
                      <TableCell>
                        {(row.liveSessionCount ?? 0) > 0 ? (
                          <span className="inline-flex items-center rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-900 dark:bg-emerald-900/40 dark:text-emerald-100">
                            En curso · {row.liveJoinedCount ?? 0} alumnos · {row.liveInProgressCount ?? 0} en evaluación · {row.liveSubmittedCount ?? 0} entregados
                          </span>
                        ) : <span className="text-muted-foreground">—</span>}
                      </TableCell>
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
                      <TableHead>
                        <TermLabel term="stats-intentos">Intentos</TermLabel>
                      </TableHead>
                      <TableHead>
                        <TermLabel term="stats-promedio">Promedio de puntaje (%)</TermLabel>
                      </TableHead>
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
                        <TableCell>
                          <StatValue value={course.attemptCount} format={formatCount} />
                        </TableCell>
                        <TableCell>
                          <StatValue value={course.averageScorePercent} format={formatPercent} />
                        </TableCell>
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
                      <TermLabel term="stats-intentos">
                        <span>Intentos: {formatCount(exam.attemptCount)}</span>
                      </TermLabel>{" "}
                      ·{" "}
                      <TermLabel term="stats-promedio">
                        <span>Promedio de puntaje: {formatPercent(exam.averageScorePercent)}</span>
                      </TermLabel>
                    </p>
                    {exam.blocks.length === 0 ? (
                      <p className="text-sm text-muted-foreground">Sin datos por bloque.</p>
                    ) : (
                      <TableShell>
                        <Table>
                          <TableHeader>
                            <TableRow>
                              <TableHead>
                                <TermLabel term="bloque">Bloque</TermLabel>
                              </TableHead>
                              <TableHead>
                                <TermLabel term="stats-correctas">Correctas</TermLabel>
                              </TableHead>
                              <TableHead>
                                <TermLabel term="stats-parciales">Parciales</TermLabel>
                              </TableHead>
                              <TableHead>
                                <TermLabel term="stats-incorrectas">Incorrectas</TermLabel>
                              </TableHead>
                              <TableHead>
                                <TermLabel term="stats-en-blanco">En blanco</TermLabel>
                              </TableHead>
                              <TableHead>
                                <TermLabel term="stats-no-calificables">No calificables</TermLabel>
                              </TableHead>
                            </TableRow>
                          </TableHeader>
                          <TableBody>
                            {exam.blocks.map(block => (
                              <TableRow key={block.blockId}>
                                <TableCell className="font-mono">
                                  <span className="block max-w-[14rem] truncate" title={block.blockId}>
                                    {block.blockId}
                                  </span>
                                </TableCell>
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
