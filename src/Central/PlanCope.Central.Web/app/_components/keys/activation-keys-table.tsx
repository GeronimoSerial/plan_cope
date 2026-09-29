"use client";

import { MoreHorizontal } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger
} from "@/components/ui/dropdown-menu";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow
} from "@/components/ui/table";
import { activationKeyStatus, type ActivationKeyTone } from "./activation-key-mapping";
import type { ActivationKeySummary } from "../../_lib/api/server";

interface ActivationKeysTableProps {
  keys: ActivationKeySummary[];
  onRevoke: (key: ActivationKeySummary) => void;
  onReissue: (key: ActivationKeySummary) => void;
}

const toneVariant: Record<ActivationKeyTone, "default" | "secondary" | "outline" | "destructive"> = {
  active: "default",
  exhausted: "secondary",
  expired: "outline",
  revoked: "destructive"
};

function formatDate(value: string): string {
  return new Date(value).toLocaleDateString("es-AR", {
    day: "2-digit",
    month: "short",
    year: "numeric"
  });
}

export function ActivationKeysTable({ keys, onRevoke, onReissue }: ActivationKeysTableProps) {
  if (keys.length === 0) {
    return (
      <p className="rounded-xl border py-10 text-center text-sm text-muted-foreground">
        Todavía no hay claves.
      </p>
    );
  }

  return (
    <div className="overflow-hidden rounded-xl border">
      <Table>
        <TableHeader className="bg-muted/40">
          <TableRow>
            <TableHead>Clave</TableHead>
            <TableHead>Emitida</TableHead>
            <TableHead>Vencimiento</TableHead>
            <TableHead>Activaciones</TableHead>
            <TableHead>Estado</TableHead>
            <TableHead className="text-right">Acciones</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {keys.map(key => {
            const status = activationKeyStatus(key);
            const revoked = Boolean(key.revokedAt);
            return (
              <TableRow key={key.id}>
                <TableCell className="font-mono font-medium">{key.keyPrefix}</TableCell>
                <TableCell>{formatDate(key.issuedAt)}</TableCell>
                <TableCell className="text-muted-foreground">
                  {key.expiresAt ? formatDate(key.expiresAt) : "Sin vencimiento"}
                </TableCell>
                <TableCell>
                  {key.activationCount} / {key.maxActivations}
                </TableCell>
                <TableCell>
                  <Badge variant={toneVariant[status.tone]}>{status.label}</Badge>
                </TableCell>
                <TableCell className="text-right">
                  {revoked ? (
                    <span className="text-muted-foreground">—</span>
                  ) : (
                    <DropdownMenu>
                      <DropdownMenuTrigger
                        render={<Button variant="ghost" size="icon-sm" aria-label="Acciones" />}
                      >
                        <MoreHorizontal />
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onClick={() => onReissue(key)}>Reemitir</DropdownMenuItem>
                        <DropdownMenuItem variant="destructive" onClick={() => onRevoke(key)}>
                          Revocar
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}
