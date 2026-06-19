import Link from "next/link";
import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { listExams, isSessionExpired } from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { ExamList } from "../../_components/exams/exam-list";
import { EmptyState } from "../../_components/ui/empty-state";
import type { ExamSummary } from "../../_lib/contracts";

export const metadata: Metadata = { title: "Exámenes · PlanCope Central" };

export default async function ExamsPage() {
  let exams: ExamSummary[];
  try {
    exams = await listExams();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  return (
    <>
      <PageHeader
        eyebrow="Exámenes"
        title="Tus exámenes"
        description="Gestioná los exámenes de la central: creá, editá versiones y publicá."
        breadcrumbs={[{ label: "Inicio", href: "/dashboard" }, { label: "Exámenes" }]}
        actions={
          <Link href="/exams/new" className="button">
            + Nuevo examen
          </Link>
        }
      />

      {exams.length === 0 ? (
        <EmptyState
          title="Aún no hay exámenes"
          description="Creá tu primer examen para empezar a construir preguntas y publicarlas."
          action={
            <Link href="/exams/new" className="button">
              Crear el primero
            </Link>
          }
        />
      ) : (
        <ExamList exams={exams} />
      )}
    </>
  );
}
