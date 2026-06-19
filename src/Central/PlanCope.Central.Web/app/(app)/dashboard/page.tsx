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

  const published = exams.filter(exam => exam.status.toLowerCase() === "published").length;
  const drafts = exams.length - published;
  const recent = exams.slice(0, 5);

  return (
    <>
      <PageHeader
        eyebrow="Inicio"
        title={`Hola, ${user?.displayName ?? ""}`}
        description="Resumen de tu actividad en la central de exámenes."
        actions={
          <Link href="/exams/new" className="button">
            + Nuevo examen
          </Link>
        }
      />

      <div className="grid grid--stats" style={{ marginBottom: "var(--space-5)" }}>
        <div className="stat">
          <div className="stat__value">{exams.length}</div>
          <div className="stat__label">Exámenes totales</div>
        </div>
        <div className="stat">
          <div className="stat__value">{published}</div>
          <div className="stat__label">Publicados</div>
        </div>
        <div className="stat">
          <div className="stat__value">{drafts}</div>
          <div className="stat__label">En preparación</div>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <h2>Exámenes recientes</h2>
          <Link href="/exams" className="button button--ghost button--sm">
            Ver todos
          </Link>
        </div>
        <div className="card__body">
          {recent.length === 0 ? (
            <EmptyState
              title="Aún no hay exámenes"
              description="Creá tu primer examen para empezar a construir preguntas."
              action={
                <Link href="/exams/new" className="button">
                  Crear el primero
                </Link>
              }
            />
          ) : (
            <div className="resource-list">
              {recent.map(exam => (
                <Link key={exam.id} href={`/exams/${exam.id}`} className="resource-card">
                  <div className="resource-card__title">
                    <span>{exam.title}</span>
                    <StatusBadge status={exam.status} />
                  </div>
                  <div className="resource-card__meta">
                    {exam.code}
                    {exam.subject ? ` · ${exam.subject}` : ""} · {exam.versionCount} versión(es)
                  </div>
                </Link>
              ))}
            </div>
          )}
        </div>
      </div>
    </>
  );
}
