import { notFound, redirect } from "next/navigation";
import type { Metadata } from "next";
import { listExams, getVersion, isSessionExpired } from "../../../../../../_lib/api/server";
import { versionToDocument } from "../../../../../../_lib/schema/mappers";
import { PageHeader } from "../../../../../../_components/layout/page-header";
import { ExamBuilder } from "../../../../../../_components/builder/exam-builder";
import type { ExamSummary, ExamVersion } from "../../../../../../_lib/contracts";

export const metadata: Metadata = { title: "Builder · PlanCope Central" };

export default async function BuilderPage({
  params
}: {
  params: Promise<{ examId: string; versionId: string }>;
}) {
  const { examId, versionId } = await params;

  let exam: ExamSummary | undefined;
  let version: ExamVersion;
  try {
    const [exams, loadedVersion] = await Promise.all([listExams(), getVersion(versionId)]);
    exam = exams.find(item => item.id === examId);
    version = loadedVersion;
  } catch (error) {
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  if (!exam || !version) {
    notFound();
  }

  const document = versionToDocument(version, exam);

  return (
    <>
      <PageHeader
        eyebrow={`${exam.code} · Versión ${version.versionNumber}`}
        title="Builder de examen"
        description="Construí las preguntas, previsualizá y publicá. Los cambios se guardan en la versión."
        breadcrumbs={[
          { label: "Inicio", href: "/dashboard" },
          { label: "Exámenes", href: "/exams" },
          { label: exam.code, href: `/exams/${examId}` },
          { label: `Versión ${version.versionNumber}` }
        ]}
      />
      <ExamBuilder versionId={version.id} status={version.status} initialDocument={document} />
    </>
  );
}
