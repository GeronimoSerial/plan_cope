import Link from "next/link";
import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { isSessionExpired, listExams } from "../../_lib/api/server";
import { getSessionUser } from "../../_lib/server/session";
import { PageHeader } from "../../_components/layout/page-header";
import { TermLabel } from "../../_components/help/term-hint";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import type { ExamSummary } from "../../_lib/contracts";
import { redirectAfterSessionExpired } from "../../_lib/server/auth-refresh";

export const metadata: Metadata = { title: "Inicio · PlanCope Central" };

const examStatusLabels: Record<string, string> = {
  draft: "Borrador",
  review: "En revisión",
  approved: "Aprobado",
  published: "Publicado",
  archived: "Archivado"
};

function examStatusLabel(status: string): string {
  return examStatusLabels[status.toLowerCase()] ?? status;
}

export default async function DashboardPage() {
  const [user, examsSettled] = await Promise.all([
    getSessionUser(),
    listExams().then(
      exams => ({ exams }),
      (error: unknown) => ({ error })
    )
  ]);

  if (!user) {
    redirect("/login");
  }

  const title = `Hola, ${user.displayName}`;
  const newExamAction = (
    <Button render={<Link href="/exams" />}>Nuevo examen</Button>
  );

  if ("error" in examsSettled) {
    if (isSessionExpired(examsSettled.error)) {
      await redirectAfterSessionExpired("/dashboard");
    }
    return (
      <>
        <PageHeader
          title={title}
          description="Resumen de los exámenes. Creá uno nuevo o retomá un borrador."
          actions={newExamAction}
        />
        <Alert variant="destructive">
          <AlertDescription>No se pudieron cargar los exámenes. Intentá nuevamente más tarde.</AlertDescription>
        </Alert>
      </>
    );
  }

  const exams = examsSettled.exams;
  const drafts = exams.filter(exam => exam.status.toLowerCase() === "draft");
  const focus = (drafts.length > 0 ? drafts : exams).slice(0, 3);
  const focusTitle = drafts.length > 0 ? "Borradores" : "Exámenes recientes";

  return (
    <>
      <PageHeader
        title={title}
        description="Resumen de los exámenes. Creá uno nuevo o retomá un borrador."
        actions={newExamAction}
      />

      <Card>
        <CardHeader>
          <CardTitle>{focusTitle}</CardTitle>
        </CardHeader>
        <CardContent>
          {focus.length === 0 ? (
            <p className="text-sm text-muted-foreground">Todavía no hay exámenes.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Título</TableHead>
                  <TableHead>Código</TableHead>
                  <TableHead>
                    <TermLabel term="estado">Estado</TermLabel>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {focus.map(exam => (
                  <TableRow key={exam.id}>
                    <TableCell className="whitespace-normal">
                      <Link href={`/exams/${exam.id}`} className="font-medium hover:underline">
                        {exam.title}
                      </Link>
                    </TableCell>
                    <TableCell className="font-mono text-muted-foreground">{exam.code}</TableCell>
                    <TableCell>
                      <Badge variant="outline">{examStatusLabel(exam.status)}</Badge>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </>
  );
}
