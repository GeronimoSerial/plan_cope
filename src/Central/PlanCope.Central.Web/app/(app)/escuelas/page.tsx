import Link from "next/link";
import type { Metadata } from "next";
import { isScopeDenied, isSessionExpired, listPagedSchoolStats, listStatsCatalog, type StatsOption, type StatsPage } from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Pagination, PaginationContent, PaginationItem, PaginationLink, PaginationNext, PaginationPrevious } from "@/components/ui/pagination";
import { Freshness, StatsMetricValue, WeightedFormulaNote } from "../../_components/schools/stats-presentation";
import { redirectAfterSessionExpired } from "../../_lib/server/auth-refresh";
import type { RawSearchParams } from "../../_lib/pagination";

export const metadata: Metadata = { title: "Escuelas · PlanCope Central" };

interface SchoolsPageProps { searchParams: Promise<RawSearchParams> }

function first(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? "" : value ?? "";
}

function pageHref(page: number, year: string, course: string): string {
  const params = new URLSearchParams();
  if (page > 1) params.set("page", String(page));
  if (year) params.set("schoolYear", year);
  if (course) params.set("course", course);
  return `/escuelas${params.size ? `?${params}` : ""}`;
}

function formatDate(value: string | null): string {
  if (!value) return "Sin fecha disponible";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Fecha no disponible" : date.toLocaleString("es-AR");
}

export default async function SchoolsPage({ searchParams }: SchoolsPageProps) {
  const params = await searchParams;
  const parsedPage = Number.parseInt(first(params?.page), 10);
  const page = Number.isFinite(parsedPage) && parsedPage > 0 ? parsedPage : 1;
  const schoolYear = first(params?.schoolYear).trim();
  const course = first(params?.course).trim();

  let result: StatsPage<Awaited<ReturnType<typeof listPagedSchoolStats>>["items"][number]>;
  let years: StatsOption[] = [];
  let courses: StatsOption[] = [];
  let loadError: "denied" | "error" | null = null;
  try {
    [result, years, courses] = await Promise.all([
      listPagedSchoolStats(page, 50, schoolYear || undefined, course || undefined),
      listStatsCatalog("year", undefined, 1, 100).then(value => value.items),
      listStatsCatalog("course", undefined, 1, 100).then(value => value.items)
    ]);
  } catch (error) {
    if (isSessionExpired(error)) await redirectAfterSessionExpired("/escuelas");
    loadError = isScopeDenied(error) ? "denied" : "error";
    result = { page: 1, pageSize: 50, totalCount: 0, items: [] };
  }

  const totalPages = Math.max(1, Math.ceil(result.totalCount / result.pageSize));
  return (
    <>
      <PageHeader title="Escuelas" description="Resultados agregados de establecimientos dentro de tu alcance." />
      {loadError && (
        <Alert variant={loadError === "denied" ? "default" : "destructive"} className="mb-4">
          <AlertDescription>{loadError === "denied" ? "No hay alcance autorizado para consultar estas escuelas." : "No se pudieron cargar las escuelas. Intentá nuevamente más tarde."}</AlertDescription>
        </Alert>
      )}
      <form method="get" className="mb-5 flex flex-wrap items-end gap-3">
        <div className="grid gap-1.5">
          <Label htmlFor="school-year">Año lectivo</Label>
          <select id="school-year" name="schoolYear" defaultValue={schoolYear} className="h-9 min-w-40 rounded-md border border-input bg-background px-3 text-sm">
            <option value="">Todos los años</option>
            {years.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </div>
        <div className="grid gap-1.5">
          <Label htmlFor="school-course">Curso / grado</Label>
          <select id="school-course" name="course" defaultValue={course} className="h-9 min-w-48 rounded-md border border-input bg-background px-3 text-sm">
            <option value="">Todos los cursos</option>
            {courses.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
          </select>
        </div>
        <Button type="submit">Aplicar filtros</Button>
      </form>

      {result.totalCount === 0 ? (
        <p className="py-8 text-center text-sm text-muted-foreground">{result.items.length === 0 && !schoolYear && !course ? "No hay escuelas dentro del alcance." : "No hay escuelas para esos filtros."}</p>
      ) : (
        <>
          <div className="overflow-x-auto rounded-xl ring-1 ring-foreground/10">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>CUE · anexo</TableHead>
                  <TableHead>Escuela</TableHead>
                  <TableHead>Localidad / departamento</TableHead>
                  <TableHead className="text-right">Intentos calificados</TableHead>
                  <TableHead className="text-right">Porcentaje ponderado</TableHead>
                  <TableHead>Sesión fresca</TableHead>
                  <TableHead>Último resultado</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.items.map(school => (
                  <TableRow key={school.cue}>
                    <TableCell className="font-mono text-sm">
                      <Link href={`/escuelas/${encodeURIComponent(school.cue)}`} className="font-medium hover:underline">{school.cue}</Link>
                      <span className="block font-sans text-xs text-muted-foreground">Anexo {school.annex ?? "—"}</span>
                    </TableCell>
                    <TableCell className="whitespace-normal">{school.schoolName || "Nombre no disponible"}</TableCell>
                    <TableCell>
                      <span className="block">{school.locality || "Localidad no disponible"}</span>
                      <span className="block text-xs text-muted-foreground">{school.department || "Departamento no disponible"}</span>
                    </TableCell>
                    <TableCell className="text-right"><StatsMetricValue metric={school.attemptCount} /></TableCell>
                    <TableCell className="text-right"><StatsMetricValue metric={school.averageScorePercent} kind="percent" /></TableCell>
                    <TableCell>{school.freshSession ? "Sí" : "No"}</TableCell>
                    <TableCell><Freshness value={school.latestRollupUpdatedAt} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <div className="mt-4 flex flex-col items-center justify-between gap-3 sm:flex-row">
            <p className="text-sm text-muted-foreground">{result.totalCount.toLocaleString("es-AR")} escuelas · página {result.page} de {totalPages}. Último resultado de esta página: {formatDate(result.items.reduce<string | null>((latest, item) => item.latestRollupUpdatedAt && (!latest || item.latestRollupUpdatedAt > latest) ? item.latestRollupUpdatedAt : latest, null))}.</p>
            {totalPages > 1 && <Pagination className="mx-0 w-auto"><PaginationContent>
              <PaginationItem><PaginationPrevious text="Anterior" href={result.page > 1 ? pageHref(result.page - 1, schoolYear, course) : undefined} aria-disabled={result.page <= 1 || undefined} tabIndex={result.page <= 1 ? -1 : undefined} className={result.page <= 1 ? "pointer-events-none opacity-50" : undefined} /></PaginationItem>
              <PaginationItem><PaginationLink href={pageHref(result.page, schoolYear, course)} isActive>{result.page}</PaginationLink></PaginationItem>
              <PaginationItem><PaginationNext text="Siguiente" href={result.page < totalPages ? pageHref(result.page + 1, schoolYear, course) : undefined} aria-disabled={result.page >= totalPages || undefined} tabIndex={result.page >= totalPages ? -1 : undefined} className={result.page >= totalPages ? "pointer-events-none opacity-50" : undefined} /></PaginationItem>
            </PaginationContent></Pagination>}
          </div>
          <div className="mt-3"><WeightedFormulaNote /></div>
        </>
      )}
    </>
  );
}
