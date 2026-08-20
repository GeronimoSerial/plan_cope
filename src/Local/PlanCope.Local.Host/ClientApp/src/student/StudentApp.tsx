import { ExamConfirmationPanel } from "./components/ExamConfirmationPanel";
import { ExamTakingPanel } from "./components/ExamTakingPanel";
import { SessionEntryPanel } from "./components/SessionEntryPanel";
import { StudentIdentityConfirmationPanel } from "./components/StudentIdentityConfirmationPanel";
import { StudentShell } from "./components/StudentShell";
import { useStudentExam } from "./hooks/useStudentExam";

export function StudentApp() {
  const exam = useStudentExam();

  return (
    <StudentShell>
      {exam.confirmationCode ? (
        <ExamConfirmationPanel code={exam.confirmationCode} submittedAt={exam.submittedAt} />
      ) : !exam.attemptId && !exam.resolution ? (
        <SessionEntryPanel
          sessionCode={exam.sessionCode}
          document={exam.document}
          isBusy={exam.isBusy}
          error={exam.error}
          onSessionCodeChange={exam.setSessionCode}
          onDocumentChange={exam.setDocument}
          onResolveStudent={exam.resolveStudent}
        />
      ) : !exam.attemptId && exam.resolution ? (
        <StudentIdentityConfirmationPanel
          student={exam.resolution.student}
          isBusy={exam.isBusy}
          error={exam.error}
          onConfirm={exam.startAttempt}
          onCorrect={exam.correctIdentity}
        />
      ) : (
        <ExamTakingPanel
          blocks={exam.blocks}
          answers={exam.answers}
          missingRequired={exam.missingRequired}
          isBusy={exam.isBusy}
          status={exam.status}
          error={exam.error}
          studentName={exam.attemptStudentName}
          onAnswerChange={exam.setAnswer}
          onSave={exam.saveAnswers}
          onSubmit={exam.submitAttempt}
        />
      )}
    </StudentShell>
  );
}
