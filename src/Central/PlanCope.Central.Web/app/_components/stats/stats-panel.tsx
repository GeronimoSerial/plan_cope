"use client";

import { Fragment, useState, type CSSProperties, type KeyboardEvent } from "react";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { Button } from "../ui/button";
import { Banner } from "../ui/banner";
import { EmptyState } from "../ui/empty-state";
import { TextField } from "../ui/text-field";
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

const tableStyle: CSSProperties = { width: "100%", borderCollapse: "collapse" };
const headRowStyle: CSSProperties = {
  textAlign: "left",
  color: "var(--text-muted)",
  fontSize: "12px",
  textTransform: "uppercase",
  letterSpacing: "0.05em"
};
const headCellStyle: CSSProperties = { padding: "0 0 var(--space-2)" };
const bodyCellStyle: CSSProperties = { padding: "var(--space-3) var(--space-1)" };
const clickableRowStyle: CSSProperties = { borderTop: "1px solid var(--line)", cursor: "pointer" };

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
    <div className="stack">
      <div className="card">
        <div className="card__header">
          <h2>Establecimientos</h2>
          <Button variant="secondary" onClick={exportCsv} disabled={schools.length === 0}>
            Exportar CSV
          </Button>
        </div>
        <div className="card__body stack">
          <div className="cols-2">
            <TextField
              label="Año lectivo (opcional)"
              placeholder="Ej. 2025"
              value={schoolYearInput}
              onChange={event => setSchoolYearInput(event.target.value)}
            />
            <TextField
              label="Curso (opcional)"
              placeholder="Ej. 4º A"
              value={courseInput}
              onChange={event => setCourseInput(event.target.value)}
            />
          </div>
          <div className="row">
            <Button onClick={() => void applyFilters()} disabled={applying}>
              {applying ? "Aplicando…" : "Aplicar filtros"}
            </Button>
          </div>

          {schoolsError && <Banner tone="error">{schoolsError}</Banner>}

          {schools.length === 0 ? (
            <EmptyState
              title="Sin estadísticas para mostrar"
              description="No hay establecimientos con datos para los filtros seleccionados."
            />
          ) : (
            <table style={tableStyle}>
              <thead>
                <tr style={headRowStyle}>
                  <th style={headCellStyle}>CUE</th>
                  <th style={headCellStyle}>Intentos</th>
                  <th style={headCellStyle}>Promedio</th>
                </tr>
              </thead>
              <tbody>
                {schools.map(row => {
                  const isSelected = selectedCue === row.cue;
                  return (
                    <Fragment key={row.cue}>
                      <tr
                        role="button"
                        tabIndex={0}
                        aria-expanded={isSelected}
                        style={clickableRowStyle}
                        onClick={() => void selectCue(row.cue)}
                        onKeyDown={activateOnKey(() => void selectCue(row.cue))}
                      >
                        <td style={{ ...bodyCellStyle, paddingLeft: 0, fontFamily: "monospace", fontWeight: 700 }}>
                          {row.cue}
                        </td>
                        <td style={bodyCellStyle}>{formatCount(row.attemptCount)}</td>
                        <td style={bodyCellStyle}>{formatPercent(row.averageScorePercent)}</td>
                      </tr>
                      {isSelected && (
                        <tr>
                          <td colSpan={3} style={{ padding: "var(--space-3) 0" }}>
                            <div className="stack">
                              <h3>Detalle del CUE {selectedCue}</h3>

                              {coursesLoading && <p className="field__hint">Cargando cursos…</p>}
                              {coursesError && <Banner tone="error">{coursesError}</Banner>}
                              {!coursesLoading && !coursesError && courses.length === 0 && (
                                <p className="field__hint">Sin datos por curso para este establecimiento.</p>
                              )}

                              {courses.length > 0 && (
                                <table style={tableStyle}>
                                  <thead>
                                    <tr style={headRowStyle}>
                                      <th style={headCellStyle}>Curso</th>
                                      <th style={headCellStyle}>Intentos</th>
                                      <th style={headCellStyle}>Promedio</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {courses.map(course => {
                                      const courseSelected = selectedCourse === course.course;
                                      return (
                                        <Fragment key={course.course}>
                                          <tr
                                            role="button"
                                            tabIndex={0}
                                            aria-expanded={courseSelected}
                                            style={clickableRowStyle}
                                            onClick={() => void selectCourse(course.course)}
                                            onKeyDown={activateOnKey(() => void selectCourse(course.course))}
                                          >
                                            <td style={{ ...bodyCellStyle, paddingLeft: 0, fontWeight: 600 }}>
                                              {course.course}
                                            </td>
                                            <td style={bodyCellStyle}>{formatCount(course.attemptCount)}</td>
                                            <td style={bodyCellStyle}>
                                              {formatPercent(course.averageScorePercent)}
                                            </td>
                                          </tr>
                                          {courseSelected && (
                                            <tr>
                                              <td colSpan={3} style={{ padding: "var(--space-3) 0" }}>
                                                <div className="stack">
                                                  {examsLoading && <p className="field__hint">Cargando exámenes…</p>}
                                                  {examsError && <Banner tone="error">{examsError}</Banner>}
                                                  {!examsLoading && !examsError && exams.length === 0 && (
                                                    <p className="field__hint">
                                                      Sin exámenes rendidos para este curso.
                                                    </p>
                                                  )}

                                                  {exams.map(exam => (
                                                    <div key={exam.examVersionId} className="stack">
                                                      <h4>
                                                        {exam.examCode} · versión {exam.versionNumber}
                                                      </h4>
                                                      <p className="field__hint">
                                                        Intentos: {formatCount(exam.attemptCount)} · Promedio:{" "}
                                                        {formatPercent(exam.averageScorePercent)}
                                                      </p>
                                                      {exam.blocks.length === 0 ? (
                                                        <p className="field__hint">Sin datos por bloque.</p>
                                                      ) : (
                                                        <table style={tableStyle}>
                                                          <thead>
                                                            <tr style={headRowStyle}>
                                                              <th style={headCellStyle}>Bloque</th>
                                                              <th style={headCellStyle}>Correctas</th>
                                                              <th style={headCellStyle}>Parciales</th>
                                                              <th style={headCellStyle}>Incorrectas</th>
                                                              <th style={headCellStyle}>En blanco</th>
                                                              <th style={headCellStyle}>No calificables</th>
                                                            </tr>
                                                          </thead>
                                                          <tbody>
                                                            {exam.blocks.map(block => (
                                                              <tr key={block.blockId} style={{ borderTop: "1px solid var(--line)" }}>
                                                                <td
                                                                  style={{
                                                                    ...bodyCellStyle,
                                                                    paddingLeft: 0,
                                                                    fontFamily: "monospace"
                                                                  }}
                                                                >
                                                                  {block.blockId}
                                                                </td>
                                                                <td style={bodyCellStyle}>{block.correctCount}</td>
                                                                <td style={bodyCellStyle}>{block.partialCount}</td>
                                                                <td style={bodyCellStyle}>{block.incorrectCount}</td>
                                                                <td style={bodyCellStyle}>{block.blankCount}</td>
                                                                <td style={bodyCellStyle}>{block.ungradableCount}</td>
                                                              </tr>
                                                            ))}
                                                          </tbody>
                                                        </table>
                                                      )}
                                                    </div>
                                                  ))}
                                                </div>
                                              </td>
                                            </tr>
                                          )}
                                        </Fragment>
                                      );
                                    })}
                                  </tbody>
                                </table>
                              )}
                            </div>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  );
}
