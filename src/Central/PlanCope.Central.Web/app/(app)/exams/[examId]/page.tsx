import { notFound, redirect } from "next/navigation";
import type { Metadata } from "next";
import { listExams, listVersions, isSessionExpired } from "../../../_lib/api/server";
import { PageHeader } from "../../../_components/layout/page-header";
import { StatusBadge } from "../../../_components/ui/status-badge";
import { VersionManager } from "../../../_components/exams/version-manager";
import type { ExamSummary, ExamVersion } from "../../../_lib/contracts";

export const metadata: Metadata = { title: "Examen · PlanCope Central" };

export default async function ExamDetailPage({ params }: { params: Promise<{ examId: string }> }) {
  const { examId } = await params;

  let exam: ExamSummary | undefined;
  let versions: ExamVersion[];
  try {
    const [exams, loadedVersions] = await Promise.all([listExams(), listVersions(examId)]);
    exam = exams.find(item => item.id === examId);
    versions = loadedVersions;
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  if (!exam) {
    notFound();
  }

  return (
    <>
      <PageHeader
        eyebrow="Examen"
        title={exam.title}
        description={[exam.code, exam.subject, exam.level].filter(Boolean).join(" · ")}
        breadcrumbs={[
          { label: "Inicio", href: "/dashboard" },
          { label: "Exámenes", href: "/exams" },
          { label: exam.code }
        ]}
        actions={<StatusBadge status={exam.status} />}
      />

      <VersionManager examId={examId} initialVersions={versions} examSubject={exam.subject ?? null} />
    </>
  );
}
