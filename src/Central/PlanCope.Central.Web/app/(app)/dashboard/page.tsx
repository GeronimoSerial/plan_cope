import Link from "next/link";
import { redirect } from "next/navigation";
import type { Metadata } from "next";
import { listExams, isSessionExpired } from "../../_lib/api/server";
import { getSessionUser } from "../../_lib/server/session";
import { PageHeader } from "../../_components/layout/page-header";
import { StatusBadge } from "../../_components/ui/status-badge";
import { EmptyState } from "../../_components/ui/empty-state";
import { Banner } from "../../_components/ui/banner";
import type { ExamSummary } from "../../_lib/contracts";

export const metadata: Metadata = { title: "Inicio · PlanCope Central" };

export default async function DashboardPage() {
  const user = await getSessionUser();

  let exams: ExamSummary[];
  try {
    exams = await listExams();
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    return (
      <>
        <PageHeader eyebrow="Inicio" title={`Hola, ${user?.displayName ?? ""}`} />
        <Banner tone="error">No se pudieron cargar los exámenes. Intentá nuevamente más tarde.</Banner>
      </>
    );
  }

  const drafts = exams.filter(exam => exam.status.toLowerCase() === "draft");
  const focus = (drafts.length > 0 ? drafts : exams).slice(0, 3);
  const focusTitle = drafts.length > 0 ? "Borradores" : "Exámenes recientes";

  return (
    <>
      <PageHeader
        eyebrow="Inicio"
        title={`Hola, ${user?.displayName ?? ""}`}
        description="Continuá con tu trabajo o creá un examen nuevo."
        actions={
          <Link href="/exams/new" className="button">
            Nuevo examen
          </Link>
        }
      />

      <div className="card">
        <div className="card__header">
          <h2>{focusTitle}</h2>
        </div>
        <div className="card__body">
          {focus.length === 0 ? (
            <EmptyState
              title="Aún no hay exámenes"
              description="Creá un examen para comenzar."
            />
          ) : (
            <div className="resource-list">
              {focus.map(exam => (
                <Link key={exam.id} href={`/exams/${exam.id}`} className="resource-card">
                  <div className="resource-card__title">
                    <span>{exam.title}</span>
                    <StatusBadge status={exam.status} />
                  </div>
                  {(exam.code || exam.subject) && (
                    <div className="resource-card__meta">{[exam.code, exam.subject].filter(Boolean).join(" · ")}</div>
                  )}
                </Link>
              ))}
            </div>
          )}
        </div>
      </div>
    </>
  );
}
