"use client";

import { useState } from "react";
import Link from "next/link";
import { toast } from "sonner";
import { TriangleAlert } from "lucide-react";
import { callCentral } from "../../_lib/api/client";
import { getErrorMessage } from "../../_lib/json";
import { scoringPolicyLabels, type ScoringPolicy } from "../../_lib/schema/exam";
import {
  applyAssignmentResult,
  assignButtonLabel,
  assignedToastMessage,
  confirmDescription,
  formatPublishedDate,
  isAllSelected,
  isSomeSelected,
  rejectedAlertMessage,
  selectionHint,
  toggleAll,
  toggleOne
} from "../../_lib/policies/legacy-policy";
import { Alert, AlertDescription } from "@/components/ui/alert";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { PolicyPicker } from "../policy/policy-picker";
import { TermLabel } from "../help/term-hint";
import type { UnassignedExamVersion } from "../../_lib/api/server";

interface LegacyPolicyPanelProps {
  initialVersions: UnassignedExamVersion[];
}

interface BulkAssignResponse {
  assignedCount: number;
  rejectedExamVersionIds: string[];
}

export function LegacyPolicyPanel({ initialVersions }: LegacyPolicyPanelProps) {
  const [versions, setVersions] = useState<UnassignedExamVersion[]>(initialVersions);
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [scoringPolicy, setScoringPolicy] = useState<ScoringPolicy | null>(null);
  const [note, setNote] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [rejectedCount, setRejectedCount] = useState(0);
  const [confirmOpen, setConfirmOpen] = useState(false);

  if (versions.length === 0) {
    return (
      <div className="grid justify-items-center gap-3 rounded-xl border py-10 text-center">
        <p className="text-sm text-muted-foreground">No hay versiones sin regla de puntaje.</p>
        <Button nativeButton={false} variant="outline" render={<Link href="/exams" />}>
          Volver a exámenes
        </Button>
      </div>
    );
  }

  const allSelected = isAllSelected(selectedIds.length, versions.length);
  const someSelected = isSomeSelected(selectedIds.length, versions.length);
  const canAssign = selectedIds.length > 0 && scoringPolicy !== null;

  async function assign() {
    if (!canAssign || scoringPolicy === null) {
      return;
    }
    setConfirmOpen(false);
    setSubmitting(true);
    setRejectedCount(0);
    try {
      const result = await callCentral<BulkAssignResponse>("admin/grading-policies/bulk-assign", {
        method: "POST",
        body: JSON.stringify({
          examVersionIds: selectedIds,
          scoringPolicy,
          note: note.trim() ? note.trim() : null
        })
      });
      toast.success(assignedToastMessage(result.assignedCount));
      setVersions(current => applyAssignmentResult(current, selectedIds, result.rejectedExamVersionIds));
      setRejectedCount(result.rejectedExamVersionIds.length);
      setSelectedIds([]);
      setScoringPolicy(null);
      setNote("");
    } catch (error) {
      toast.error(getErrorMessage(error, "No se pudieron asignar las reglas de puntaje."));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="grid gap-6">
      {rejectedCount > 0 && (
        <Alert variant="destructive">
          <TriangleAlert />
          <AlertDescription>{rejectedAlertMessage(rejectedCount)}</AlertDescription>
        </Alert>
      )}

      <div className="overflow-hidden rounded-xl border">
        <Table>
          <TableHeader className="bg-muted/40">
            <TableRow>
              <TableHead className="w-10">
                <Checkbox
                  aria-label="Seleccionar todas las versiones"
                  checked={allSelected}
                  indeterminate={someSelected}
                  onCheckedChange={checked => setSelectedIds(toggleAll(versions.map(v => v.examVersionId), checked === true))}
                />
              </TableHead>
              <TableHead>Examen</TableHead>
              <TableHead>
                <TermLabel term="version">Versión</TermLabel>
              </TableHead>
              <TableHead>Publicada</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {versions.map(version => (
              <TableRow key={version.examVersionId}>
                <TableCell>
                  <Checkbox
                    aria-label={`Seleccionar ${version.examCode} versión ${version.versionNumber}`}
                    checked={selectedIds.includes(version.examVersionId)}
                    onCheckedChange={checked =>
                      setSelectedIds(current => toggleOne(current, version.examVersionId, checked === true))
                    }
                  />
                </TableCell>
                <TableCell className="font-mono font-medium">{version.examCode}</TableCell>
                <TableCell>v{version.versionNumber}</TableCell>
                <TableCell>{formatPublishedDate(version.publishedAt)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>
            <TermLabel term="regla-puntaje">Regla a asignar</TermLabel>
          </CardTitle>
        </CardHeader>
        <CardContent className="grid gap-4">
          <PolicyPicker
            value={scoringPolicy}
            onChange={setScoringPolicy}
            disabled={submitting}
            idPrefix="legacy-policy"
          />

          <div className="grid gap-1.5">
            <Label htmlFor="legacy-policy-note">Nota (opcional)</Label>
            <Textarea
              id="legacy-policy-note"
              value={note}
              onChange={event => setNote(event.target.value)}
              placeholder="Ej. Migración de exámenes publicados antes de la regla."
              disabled={submitting}
            />
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <Button disabled={!canAssign || submitting} onClick={() => setConfirmOpen(true)}>
              {submitting ? "Asignando…" : assignButtonLabel(selectedIds.length)}
            </Button>
            <span className="text-xs text-muted-foreground">{selectionHint(selectedIds.length)}</span>
          </div>
        </CardContent>
      </Card>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Asignar regla de puntaje</AlertDialogTitle>
            <AlertDialogDescription>
              {scoringPolicy !== null && confirmDescription(scoringPolicyLabels[scoringPolicy], selectedIds.length)}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={submitting}>Cancelar</AlertDialogCancel>
            <AlertDialogAction disabled={submitting} onClick={() => void assign()}>
              Asignar
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
