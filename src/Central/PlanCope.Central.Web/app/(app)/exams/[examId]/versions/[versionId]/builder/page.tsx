import { notFound, redirect } from "next/navigation";
import type { Metadata } from "next";
import { listExams, listVersions, getVersion, isSessionExpired } from "../../../../../../_lib/api/server";
import { versionToDocument } from "../../../../../../_lib/schema/mappers";
import { findDraft } from "../../../../../../_lib/exams/version-state";
import { ExamBuilder } from "../../../../../../_components/builder/exam-builder";
import type { ExamSummary, ExamVersion } from "../../../../../../_lib/contracts";

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
    if (isSessionExpired(error)) {
      redirect("/login?expired=1");
    }
    throw error;
  }

  if (!exam || !version) {
    notFound();
  }

  const nextVersionNumber = versions.reduce((max, item) => Math.max(max, item.versionNumber), 0) + 1;
  const draft = findDraft(versions);
  const document = versionToDocument(version, exam);

  return (
    <ExamBuilder
      examId={examId}
      examCode={exam.code}
      examTitle={exam.title}
      versionId={version.id}
      versionNumber={version.versionNumber}
      status={version.status}
      isCurrent={version.isCurrent}
      basedOnVersionNumber={version.basedOnVersionNumber ?? null}
      currentPublishedVersionNumber={exam.publishedVersionNumber ?? null}
      nextVersionNumber={nextVersionNumber}
      draftVersionId={draft?.id ?? null}
      draftVersionNumber={draft?.versionNumber ?? null}
      initialDocument={document}
      canPublish={version.canPublish}
      publishBlockedReason={version.publishBlockedReason}
      autoOpenPublish={publicar === "1" && version.canPublish}
    />
  );
}
