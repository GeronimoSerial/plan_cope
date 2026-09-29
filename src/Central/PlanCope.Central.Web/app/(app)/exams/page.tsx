import { redirect } from "next/navigation";
import type { Metadata } from "next";
import {
  isSessionExpired,
  listExams,
  listUnassignedGradingPolicies,
  type UnassignedExamVersion
} from "../../_lib/api/server";
import { PageHeader } from "../../_components/layout/page-header";
import { CreateExamButton } from "../../_components/exams/create-exam-dialog";
import { ExamsTable } from "../../_components/exams/exams-table";
import { ExamsUnassignedAlert } from "../../_components/exams/exams-unassigned-alert";
import type { ExamSummary } from "../../_lib/contracts";
import { redirectAfterSessionExpired } from "../../_lib/server/auth-refresh";
import { getSessionUser } from "../../_lib/server/session";
import { canEditExams } from "../../_lib/exam-permissions";

export const metadata: Metadata = { title: "Exámenes · PlanCope Central" };

export default async function ExamsPage() {
  let exams: ExamSummary[];
  let unassigned: UnassignedExamVersion[];
  try {
    [exams, unassigned] = await Promise.all([
      listExams(),
      listUnassignedGradingPolicies().catch(() => [] as UnassignedExamVersion[])
    ]);
  } catch (error) {
    if (isSessionExpired(error)) {
      await redirectAfterSessionExpired("/exams");
    }
    throw error;
  }
  const canEdit = canEditExams((await getSessionUser())?.role);

  return (
    <>
      <PageHeader
        title="Exámenes"
        description="Exámenes del sistema. Creá uno, cargá sus preguntas y publicalo para enviarlo a los nodos."
        actions={<CreateExamButton canEditExams={canEdit} />}
      />
      <ExamsUnassignedAlert count={unassigned.length} />
      <ExamsTable exams={exams} canEditExams={canEdit} />
    </>
  );
}
