import Link from "next/link";
import type { Metadata } from "next";
import { isScopeDenied, isSessionExpired, getSchoolStatsDetail, getStatsAggregate, listStatsCatalog, type StatsAggregate, type StatsOption, type SchoolStatsDetail } from "../../../_lib/api/server";
import { PageHeader } from "../../../_components/layout/page-header";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Pagination, PaginationContent, PaginationItem, PaginationLink, PaginationNext, PaginationPrevious } from "@/components/ui/pagination";
import { Freshness, StatsMetricValue, WeightedFormulaNote } from "../../../_components/schools/stats-presentation";
import { redirectAfterSessionExpired } from "../../../_lib/server/auth-refresh";
import type { RawSearchParams } from "../../../_lib/pagination";

export const metadata: Metadata = { title: "Detalle de escuela · PlanCope Central" };

interface SchoolDetailPageProps {
  params: Promise<{ cue: string }>;
  searchParams: Promise<RawSearchParams>;
}

function first(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? "" : value ?? "";
}

function optionValue(options: StatsOption[], value: string, fallback: string): string {
  return options.find(option => option.value === value)?.label ?? fallback;
}

function pageHref(cue: string, page: number, filters: Record<string, string>): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filters)) if (value) params.set(key, value);
  if (page > 1) params.set("page", String(page));
  return `/escuelas/${encodeURIComponent(cue)}${params.size ? `?${params}` : ""}`;
}

export default async function SchoolDetailPage({ params, searchParams }: SchoolDetailPageProps) {
  const [{ cue }, rawParams] = await Promise.all([params, searchParams]);
  const schoolYear = first(rawParams?.schoolYear).trim();
  const course = first(rawParams?.course).trim();
  const subject = first(rawParams?.subject).trim();
  const version = first(rawParams?.version).trim();
  const parsedPage = Number.parseInt(first(rawParams?.page), 10);
  const page = Number.isFinite(parsedPage) && parsedPage > 0 ? parsedPage : 1;

  let school: SchoolStatsDetail | null = null;
  let aggregate: StatsAggregate | null = null;
  let years: StatsOption[] = [];
  let courses: StatsOption[] = [];
  let subjects: StatsOption[] = [];
  let versions: StatsOption[] = [];
  let loadError: "denied" | "error" | null = null;
  try {
    [school, aggregate, years, courses, subjects, versions] = await Promise.all([
      getSchoolStatsDetail(cue, schoolYear || undefined, course || undefined),
      getStatsAggregate({ groupBy: "version", school: cue, schoolYear: schoolYear || undefined, course: course || undefined, subject: subject || undefined, version: version || undefined, page, pageSize: 50 }),
      listStatsCatalog("year", undefined, 1, 100).then(result => result.items),
      listStatsCatalog("course", undefined, 1, 100).then(result => result.items),
      listStatsCatalog("subject", undefined, 1, 100).then(result => result.items),
      listStatsCatalog("version", undefined, 1, 100).then(result => result.items)
    ]);
  } catch (error) {
    if (isSessionExpired(error)) await redirectAfterSessionExpired(`/escuelas/${encodeURIComponent(cue)}`);
    loadError = isScopeDenied(error) ? "denied" : "error";
  }

  if (loadError) {
    return (
      <>
        <PageHeader title="Detalle de escuela" />
        <Alert variant={loadError === "denied" ? "default" : "destructive"}>
          <AlertDescription>{loadError === "denied" ? "Esta escuela no está dentro del alcance autorizado." : "No se pudo cargar el detalle. Intentá nuevamente más tarde."}</AlertDescription>
        </Alert>
        <Button nativeButton={false} render={<Link href="/escuelas" />} variant="outline" className="mt-4">Volver a escuelas</Button>
      </>
    );
  }

  const hasFilters = Boolean(schoolYear || course || subject || version);
  const totalPages = aggregate ? Math.max(1, Math.ceil(aggregate.totalCount / aggregate.pageSize)) : 1;
  const filters = { schoolYear, course, subject, version };
  const title = school?.schoolName || `Escuela ${cue}`;
  return (
    <>
      <PageHeader title={title} description={`CUE ${school?.cue ?? cue}`} />
      <div className="mb-4 flex flex-wrap gap-x-5 gap-y-1 text-sm text-muted-foreground">
        <span>Localidad: {school?.locality || "No disponible"}</span>
        <span>Departamento: {school?.department || "No disponible"}</span>
      </div>
      <Link href="/escuelas" className="mb-5 inline-block text-sm font-medium underline-offset-4 hover:underline">Volver a escuelas</Link>

      <form method="get" className="mb-5 flex flex-wrap items-end gap-3">
        <div className="grid gap-1.5">
          <Label htmlFor="school-year">Año lectivo</Label>
          <select id="school-year" name="schoolYear" defaultValue={schoolYear} className="h-9 min-w-36 rounded-md border border-input bg-background px-3 text-sm">
            <option value="">Todos los años</option>{years.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="school-course">Curso / grado</Label>
          <select id="school-course" name="course" defaultValue={course} className="h-9 min-w-44 rounded-md border border-input bg-background px-3 text-sm">
            <option value="">Todos los cursos</option>{courses.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="school-subject">Materia</Label>
          <select id="school-subject" name="subject" defaultValue={subject} className="h-9 min-w-44 rounded-md border border-input bg-background px-3 text-sm">
            <option value="">Todas las materias</option>{subjects.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="school-version">Examen / versión</Label>
          <select id="school-version" name="version" defaultValue={version} className="h-9 min-w-48 rounded-md border border-input bg-background px-3 text-sm">
            <option value="">Todas las versiones</option>{versions.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </div>
        <Button type="submit">Aplicar filtros</Button>
      </form>

      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-lg font-semibold tracking-tight">Resultados por versión de examen</h2>
        <p className="text-xs text-muted-foreground">Actualizado: <Freshness value={aggregate?.latestRollupUpdatedAt ?? null} /></p>
      </div>
      <p className="mb-4 text-sm text-muted-foreground">
        {hasFilters ? `Filtros: ${[schoolYear && optionValue(years, schoolYear, schoolYear), course && optionValue(courses, course, course), subject && optionValue(subjects, subject, subject), version && optionValue(versions, version, version)].filter(Boolean).join(" · ")}.` : "Los filtros se aplican en el agregado server-side por escuela; las combinaciones no disponibles en detail usan el endpoint agregado."} No se muestran alumnos ni intentos individuales.
      </p>
      {aggregate?.dataState === "no_results" || !aggregate?.rows.length ? (
        <p className="rounded-lg border border-dashed p-8 text-center text-sm text-muted-foreground">No hay resultados para esta escuela y esos filtros. Esto representa un conjunto sin resultados, distinto de un cero medido o una métrica suprimida.</p>
      ) : (
        <>
          <div className="overflow-x-auto rounded-xl ring-1 ring-foreground/10">
            <Table>
              <TableHeader><TableRow><TableHead>Versión</TableHead><TableHead className="text-right">Intentos calificados</TableHead><TableHead className="text-right">Porcentaje ponderado</TableHead></TableRow></TableHeader>
              <TableBody>{aggregate.rows.map(row => <TableRow key={row.key}>
                <TableCell className="whitespace-normal">{row.label}</TableCell>
                <TableCell className="text-right"><StatsMetricValue metric={row.attemptCount} /></TableCell>
                <TableCell className="text-right"><StatsMetricValue metric={row.weightedScorePercent} kind="percent" /></TableCell>
              </TableRow>)}</TableBody>
            </Table>
          </div>
          {totalPages > 1 && <div className="mt-4 flex flex-col items-center justify-between gap-3 sm:flex-row">
            <p className="text-sm text-muted-foreground">{aggregate.totalCount.toLocaleString("es-AR")} versiones · página {aggregate.page} de {totalPages}.</p>
            <Pagination className="mx-0 w-auto"><PaginationContent>
              <PaginationItem><PaginationPrevious text="Anterior" href={aggregate.page > 1 ? pageHref(cue, aggregate.page - 1, filters) : undefined} aria-disabled={aggregate.page <= 1 || undefined} tabIndex={aggregate.page <= 1 ? -1 : undefined} className={aggregate.page <= 1 ? "pointer-events-none opacity-50" : undefined} /></PaginationItem>
              <PaginationItem><PaginationLink href={pageHref(cue, aggregate.page, filters)} isActive>{aggregate.page}</PaginationLink></PaginationItem>
              <PaginationItem><PaginationNext text="Siguiente" href={aggregate.page < totalPages ? pageHref(cue, aggregate.page + 1, filters) : undefined} aria-disabled={aggregate.page >= totalPages || undefined} tabIndex={aggregate.page >= totalPages ? -1 : undefined} className={aggregate.page >= totalPages ? "pointer-events-none opacity-50" : undefined} /></PaginationItem>
            </PaginationContent></Pagination>
          </div>}
        </>
      )}
      <div className="mt-4"><WeightedFormulaNote /></div>
    </>
  );
}
