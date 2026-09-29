import { notFound, redirect } from "next/navigation";
import type { Metadata } from "next";
import { isNotFound, isSessionExpired, listExams, listVersions } from "../../../_lib/api/server";
import { PageHeader } from "../../../_components/layout/page-header";
import { Badge } from "@/components/ui/badge";
import { ExamHeaderActions } from "../../../_components/exams/exam-header-actions";
import { ExamVersionsTable } from "../../../_components/exams/exam-versions-table";
import { TermLabel } from "../../../_components/help/term-hint";
import {
  formatPublishedAt,
  formatReceivedBy,
  publicationStateBadgeVariant,
  publicationStateLabel,
  publicationStateTerm
} from "../../../_lib/exams/exam-state";
import type { ExamSummary, ExamVersion } from "../../../_lib/contracts";
import { redirectAfterSessionExpired } from "../../../_lib/server/auth-refresh";
import { getSessionUser } from "../../../_lib/server/session";
import { canEditExams } from "../../../_lib/exam-permissions";

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
    if (isNotFound(error)) {
      notFound();
    }
    if (isSessionExpired(error)) {
      await redirectAfterSessionExpired(`/exams/${examId}`);
    }
    throw error;
  }

  if (!exam) {
    notFound();
  }

  const published = exam.publicationState === "published";

  return (
    <>
      <PageHeader
        title={exam.title}
        description={exam.code}
        actions={<ExamHeaderActions exam={exam} versions={versions} canEditExams={canEditExams((await getSessionUser())?.role)} />}
      />

      <div className="mb-6 flex flex-wrap items-center gap-2">
        <TermLabel term={publicationStateTerm(exam.publicationState)}>
          <Badge variant={publicationStateBadgeVariant(exam.publicationState)}>
            {publicationStateLabel(exam.publicationState)}
          </Badge>
        </TermLabel>
        {published && (
          <span className="inline-flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <span>Publicado el {formatPublishedAt(exam.publishedAt)}</span>
            <span aria-hidden="true">·</span>
            <TermLabel term="recibido-por-nodos">{formatReceivedBy(exam.pulledByNodeCount)}</TermLabel>
          </span>
        )}
      </div>

      <ExamVersionsTable examId={exam.id} versions={versions} canEditExams={canEditExams((await getSessionUser())?.role)} />
    </>
  );
}
