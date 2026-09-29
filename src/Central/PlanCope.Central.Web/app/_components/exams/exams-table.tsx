"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { Search } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { CreateExamButton } from "./create-exam-dialog";
import { TermLabel } from "../help/term-hint";
import {
  filterExams,
  formatPublishedAt,
  formatReceivedBy,
  publicationStateBadgeVariant,
  publicationStateLabel,
  publicationStateTerm
} from "../../_lib/exams/exam-state";
import type { ExamSummary } from "../../_lib/contracts";

interface ExamsTableProps {
  exams: ExamSummary[];
  canEditExams: boolean;
}

export function ExamsTable({ exams, canEditExams }: ExamsTableProps) {
  const [query, setQuery] = useState("");
  const filtered = useMemo(() => filterExams(exams, query), [exams, query]);

  if (exams.length === 0) {
    return (
      <div className="grid justify-items-center gap-3 rounded-xl border py-10 text-center">
        <p className="text-sm text-muted-foreground">
          Todavía no hay exámenes. Creá el primero para cargar preguntas y publicarlo.
        </p>
        <CreateExamButton canEditExams={canEditExams} />
      </div>
    );
  }

  return (
    <>
      <div className="relative mb-4 max-w-sm">
        <Search
          className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <Input
          className="pl-8"
          value={query}
          onChange={event => setQuery(event.target.value)}
          placeholder="Buscar por título o código"
          aria-label="Buscar exámenes"
        />
      </div>

      {filtered.length === 0 ? (
        <p className="rounded-xl border py-10 text-center text-sm text-muted-foreground">
          No hay exámenes que coincidan con la búsqueda.
        </p>
      ) : (
        <div className="overflow-x-auto rounded-xl border">
          <Table>
            <TableHeader className="bg-muted/40">
              <TableRow>
                <TableHead>Título</TableHead>
                <TableHead className="hidden md:table-cell">Código</TableHead>
                <TableHead>
                  <TermLabel term="estado">Estado</TermLabel>
                </TableHead>
                <TableHead className="hidden md:table-cell">
                  <TermLabel term="publicar">Publicación</TermLabel>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {filtered.map(exam => {
                const published = exam.publicationState === "published";
                const received = formatReceivedBy(exam.pulledByNodeCount);
                return (
                  <TableRow key={exam.id}>
                    <TableCell className="whitespace-normal font-medium">
                      <div className="flex flex-col gap-0.5">
                        <Link className="hover:underline" href={`/exams/${exam.id}`}>
                          {exam.title}
                        </Link>
                        <span className="font-mono text-xs text-muted-foreground md:hidden">{exam.code}</span>
                        {published && (
                          <span className="text-xs text-muted-foreground md:hidden">
                            Publicado el {formatPublishedAt(exam.publishedAt)}
                          </span>
                        )}
                        {published && received && (
                          <span className="md:hidden">
                            <TermLabel term="recibido-por-nodos">
                              <span className="text-xs text-muted-foreground">{received}</span>
                            </TermLabel>
                          </span>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="hidden font-mono text-muted-foreground md:table-cell">
                      {exam.code}
                    </TableCell>
                    <TableCell>
                      <TermLabel term={publicationStateTerm(exam.publicationState)}>
                        <Badge variant={publicationStateBadgeVariant(exam.publicationState)}>
                          {publicationStateLabel(exam.publicationState)}
                        </Badge>
                      </TermLabel>
                    </TableCell>
                    <TableCell className="hidden whitespace-normal text-muted-foreground md:table-cell">
                      {published ? (
                        <div className="flex flex-col">
                          <span>Publicado el {formatPublishedAt(exam.publishedAt)}</span>
                          <TermLabel term="recibido-por-nodos">
                            <span className="text-xs">{received}</span>
                          </TermLabel>
                        </div>
                      ) : (
                        <span>—</span>
                      )}
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </>
  );
}
