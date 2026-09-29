"use client";

import { useState } from "react";
import Link from "next/link";
import { MoreHorizontal } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger
} from "@/components/ui/dropdown-menu";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { TermLabel } from "../help/term-hint";
import { formatPublishedAt, publishBlockedMessage } from "../../_lib/exams/exam-state";
import {
  findDraft,
  nextVersionNumber,
  versionStatusBadgeVariant,
  versionStatusLabel,
  versionStatusTerm
} from "../../_lib/exams/version-state";
import { CreateVersionDialog } from "./create-version-dialog";
import type { ExamVersion } from "../../_lib/contracts";

interface ExamVersionsTableProps {
  examId: string;
  versions: ExamVersion[];
}

export function ExamVersionsTable({ examId, versions }: ExamVersionsTableProps) {
  const [createSource, setCreateSource] = useState<ExamVersion | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  if (versions.length === 0) {
    return (
      <p className="rounded-xl border py-10 text-center text-sm text-muted-foreground">
        Todavía no hay versiones.
      </p>
    );
  }

  const draft = findDraft(versions);
  const nextNumber = nextVersionNumber(versions);

  function openCreateFrom(version: ExamVersion) {
    setCreateSource(version);
    setCreateOpen(true);
  }

  return (
    <>
      <div className="overflow-x-auto rounded-xl border">
        <Table>
          <TableHeader className="bg-muted/40">
            <TableRow>
              <TableHead>
                <TermLabel term="version">Versión</TermLabel>
              </TableHead>
              <TableHead>
                <TermLabel term="estado">Estado</TermLabel>
              </TableHead>
              <TableHead className="hidden md:table-cell">
                <TermLabel term="bloque">Bloques</TermLabel>
              </TableHead>
              <TableHead className="hidden md:table-cell">Publicada</TableHead>
              <TableHead className="text-right">Acciones</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {versions.map(version => {
              const builderHref = `/exams/${examId}/versions/${version.id}/builder`;
              const label = versionStatusLabel(version);
              const isDraft = label === "Borrador";
              const publishedDate = version.publishedAt ? formatPublishedAt(version.publishedAt) : null;
              const supersededDate = version.supersededAt ? formatPublishedAt(version.supersededAt) : null;
              const blockedMessage = publishBlockedMessage(version.publishBlockedReason);
              return (
                <TableRow key={version.id}>
                  <TableCell className="font-mono font-medium">
                    <div className="flex flex-col gap-0.5">
                      <span>v{version.versionNumber}</span>
                      {version.basedOnVersionNumber != null && (
                        <TermLabel term="basada-en">
                          <span className="font-sans text-xs font-normal text-muted-foreground">
                            basada en v{version.basedOnVersionNumber}
                          </span>
                        </TermLabel>
                      )}
                      <span className="font-sans text-xs font-normal text-muted-foreground md:hidden">
                        {version.blockCount} {version.blockCount === 1 ? "bloque" : "bloques"}
                      </span>
                      {publishedDate && (
                        <span className="font-sans text-xs font-normal text-muted-foreground md:hidden">
                          Publicada el {publishedDate}
                        </span>
                      )}
                      {supersededDate && (
                        <span className="font-sans text-xs font-normal text-muted-foreground md:hidden">
                          reemplazada el {supersededDate}
                        </span>
                      )}
                    </div>
                  </TableCell>
                  <TableCell>
                    <TermLabel term={versionStatusTerm(version)}>
                      <Badge variant={versionStatusBadgeVariant(version)}>{label}</Badge>
                    </TermLabel>
                  </TableCell>
                  <TableCell className="hidden md:table-cell">{version.blockCount}</TableCell>
                  <TableCell className="hidden text-muted-foreground md:table-cell">
                    {publishedDate ? (
                      <div className="flex flex-col">
                        <span>{publishedDate}</span>
                        {supersededDate && (
                          <span className="text-xs">reemplazada el {supersededDate}</span>
                        )}
                      </div>
                    ) : (
                      "—"
                    )}
                  </TableCell>
                  <TableCell className="text-right">
                    <DropdownMenu>
                      <DropdownMenuTrigger render={<Button variant="ghost" size="icon-sm" aria-label="Acciones" />}>
                        <MoreHorizontal />
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem render={<Link href={builderHref} />}>
                          {isDraft ? "Editar" : "Ver"}
                        </DropdownMenuItem>
                        {isDraft &&
                          (version.canPublish ? (
                            <DropdownMenuItem render={<Link href={`${builderHref}?publicar=1`} />}>
                              Publicar
                            </DropdownMenuItem>
                          ) : (
                            <DropdownMenuItem disabled>
                              <span className="grid gap-0.5">
                                <span>Publicar</span>
                                <span className="text-xs font-normal text-muted-foreground">
                                  {blockedMessage || "No se puede publicar todavía"}
                                </span>
                              </span>
                            </DropdownMenuItem>
                          ))}
                        <DropdownMenuSeparator />
                        <DropdownMenuItem onClick={() => openCreateFrom(version)}>
                          Crear versión a partir de esta
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>

      {createSource && (
        <CreateVersionDialog
          examId={examId}
          open={createOpen}
          onOpenChange={setCreateOpen}
          sourceVersionId={createSource.id}
          sourceNumber={createSource.versionNumber}
          sourcePublished={versionStatusLabel(createSource) !== "Borrador"}
          nextNumber={nextNumber}
          draft={draft ? { id: draft.id, versionNumber: draft.versionNumber } : null}
        />
      )}
    </>
  );
}
