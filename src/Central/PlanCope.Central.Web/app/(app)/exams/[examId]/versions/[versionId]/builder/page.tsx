import { notFound } from "next/navigation";
import type { Metadata } from "next";
import { listExams, listVersions, getVersion, isNotFound, isSessionExpired } from "../../../../../../_lib/api/server";
import { versionToDocument } from "../../../../../../_lib/schema/mappers";
import { ExamBuilder } from "../../../../../../_components/builder/exam-builder";
import type { ExamSummary, ExamVersion } from "../../../../../../_lib/contracts";
import { redirectAfterSessionExpired } from "../../../../../../_lib/server/auth-refresh";
import { getSessionUser } from "../../../../../../_lib/server/session";
import { canEditExams } from "../../../../../../_lib/exam-permissions";

export const metadata: Metadata = { title: "Builder · PlanCope Central" };

export default async function BuilderPage({
  params,
  searchParams
}: {
  params: Promise<{ examId: string; versionId: string }>;
  searchParams: Promise<{ publicar?: string }>;
}) {
  const { examId, versionId } = await params;
  const { publicar } = await searchParams;

  let exam: ExamSummary | undefined;
  let version: ExamVersion;
  let versions: ExamVersion[];
  try {
    const [exams, loadedVersion, loadedVersions] = await Promise.all([
      listExams(),
      getVersion(versionId),
      listVersions(examId)
    ]);
    exam = exams.find(item => item.id === examId);
    version = loadedVersion;
    versions = loadedVersions;
  } catch (error) {
    if (isNotFound(error)) {
      notFound();
    }
    if (isSessionExpired(error)) {
      await redirectAfterSessionExpired(`/exams/${examId}/versions/${versionId}/builder`);
    }
    throw error;
  }

  if (!exam || !version) {
    notFound();
  }

  const nextVersionNumber = versions.reduce((max, item) => Math.max(max, item.versionNumber), 0) + 1;
  const document = versionToDocument(version, exam);

  return (
    <ExamBuilder
      examId={examId}
      examTitle={exam.title}
      versionId={version.id}
      versionNumber={version.versionNumber}
      status={version.status}
      isCurrent={version.isCurrent}
      basedOnVersionNumber={version.basedOnVersionNumber ?? null}
      currentPublishedVersionNumber={exam.publishedVersionNumber ?? null}
      nextVersionNumber={nextVersionNumber}
      initialDocument={document}
      canPublish={version.canPublish}
      publishBlockedReason={version.publishBlockedReason}
      autoOpenPublish={publicar === "1" && version.canPublish}
      canEditExams={canEditExams((await getSessionUser())?.role)}
    />
  );
}
