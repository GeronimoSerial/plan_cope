import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

export default function Loading() {
  return (
    <div>
      <div className="mb-6 grid gap-2">
        <Skeleton className="h-8 w-72" />
        <Skeleton className="h-4 w-80" />
      </div>
      <div className="overflow-hidden rounded-xl border">
        <Table>
          <TableHeader className="bg-muted/40">
            <TableRow>
              <TableHead className="w-10" />
              <TableHead>Examen</TableHead>
              <TableHead>Versión</TableHead>
              <TableHead>Publicada</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {Array.from({ length: 6 }).map((_, index) => (
              <TableRow key={index}>
                <TableCell>
                  <Skeleton className="size-4" />
                </TableCell>
                <TableCell>
                  <Skeleton className="h-4 w-32" />
                </TableCell>
                <TableCell>
                  <Skeleton className="h-4 w-10" />
                </TableCell>
                <TableCell>
                  <Skeleton className="h-4 w-24" />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}
