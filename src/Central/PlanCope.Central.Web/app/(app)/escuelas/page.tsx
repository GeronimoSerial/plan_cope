import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { isSessionExpired, listSchools, type SchoolSummary } from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationItem,
  PaginationLink,
  PaginationNext,
  PaginationPrevious
} from "@/components/ui/pagination";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import {
  buildPageHref,
  buildPageWindow,
  filterByQuery,
  formatShowing,
  paginate,
  parsePageParams,
  type RawSearchParams
} from "../../_lib/pagination";

export const metadata: Metadata = { title: "Escuelas · PlanCope Central" };

interface SchoolsPageProps {
  searchParams: Promise<RawSearchParams>;
}

function schoolStatusLabel(status: string): string {
  const normalized = status.trim().toLowerCase();
  if (normalized === "active") {
    return "Activa";
  }
  if (normalized === "inactive") {
    return "Inactiva";
  }
  return status;
}

function annexLabel(annex: number | null | undefined): string {
  return annex === null || annex === undefined ? "—" : String(annex);
}

export default async function SchoolsPage({ searchParams }: SchoolsPageProps) {
  const { q, page, pageSize } = parsePageParams(await searchParams);

  let schools: SchoolSummary[];
  try {
    schools = await listSchools();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  const filtered = filterByQuery(schools, q, school => [school.cue, school.name]);
  const result = paginate(filtered, page, pageSize);
  const pageWindow = buildPageWindow(result.page, result.totalPages);
  const hasAnnex = filtered.some(school => school.annex !== null && school.annex !== undefined);

  return (
    <>
      <PageHeader title="Escuelas" />

      <form method="get" role="search" className="mb-4 flex max-w-md items-end gap-2">
        <div className="grid flex-1 gap-1.5">
          <Label htmlFor="escuelas-q">Buscar</Label>
          <Input id="escuelas-q" name="q" defaultValue={q} placeholder="CUE o nombre" />
        </div>
        <Button type="submit">Buscar</Button>
      </form>

      {result.total === 0 ? (
        <p className="text-sm text-muted-foreground">
          {q ? "No hay escuelas que coincidan con la búsqueda." : "No hay escuelas."}
        </p>
      ) : (
        <>
          <div className="overflow-hidden rounded-xl ring-1 ring-foreground/10">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>CUE</TableHead>
                  <TableHead>Nombre</TableHead>
                  {hasAnnex && <TableHead>Anexo</TableHead>}
                  <TableHead>Estado</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {result.items.map(school => (
                  <TableRow key={school.id}>
                    <TableCell className="font-mono font-medium">{school.cue}</TableCell>
                    <TableCell className="whitespace-normal">{school.name}</TableCell>
                    {hasAnnex && <TableCell>{annexLabel(school.annex)}</TableCell>}
                    <TableCell>
                      <Badge variant="outline">{schoolStatusLabel(school.status)}</Badge>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>

          <div className="mt-4 flex flex-col items-center justify-between gap-3 sm:flex-row">
            <p className="text-sm text-muted-foreground">{formatShowing(result.from, result.to, result.total)}</p>
            {result.totalPages > 1 && (
              <Pagination className="mx-0 w-auto">
                <PaginationContent>
                  <PaginationItem>
                    <PaginationPrevious
                      text="Anterior"
                      href={result.page > 1 ? buildPageHref("/escuelas", q, result.page - 1) : undefined}
                      aria-disabled={result.page <= 1 || undefined}
                      tabIndex={result.page <= 1 ? -1 : undefined}
                      className={result.page <= 1 ? "pointer-events-none opacity-50" : undefined}
                    />
                  </PaginationItem>
                  {pageWindow.map((item, index) =>
                    item === "ellipsis" ? (
                      <PaginationItem key={`ellipsis-${index}`}>
                        <PaginationEllipsis />
                      </PaginationItem>
                    ) : (
                      <PaginationItem key={item}>
                        <PaginationLink
                          href={buildPageHref("/escuelas", q, item)}
                          isActive={item === result.page}
                        >
                          {item}
                        </PaginationLink>
                      </PaginationItem>
                    )
                  )}
                  <PaginationItem>
                    <PaginationNext
                      text="Siguiente"
                      href={
                        result.page < result.totalPages
                          ? buildPageHref("/escuelas", q, result.page + 1)
                          : undefined
                      }
                      aria-disabled={result.page >= result.totalPages || undefined}
                      tabIndex={result.page >= result.totalPages ? -1 : undefined}
                      className={result.page >= result.totalPages ? "pointer-events-none opacity-50" : undefined}
                    />
                  </PaginationItem>
                </PaginationContent>
              </Pagination>
            )}
          </div>
        </>
      )}
    </>
  );
}
