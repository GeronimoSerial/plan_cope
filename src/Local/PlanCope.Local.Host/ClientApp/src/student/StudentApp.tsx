import { ExamConfirmationPanel } from "./components/ExamConfirmationPanel";
import { ExamTakingPanel } from "./components/ExamTakingPanel";
import { SessionEntryPanel } from "./components/SessionEntryPanel";
import { StudentShell } from "./components/StudentShell";
import { useStudentExam } from "./hooks/useStudentExam";

export function StudentApp() {
  const exam = useStudentExam();

  return (
    <StudentShell>
      {exam.confirmationCode ? (
        <ExamConfirmationPanel code={exam.confirmationCode} submittedAt={exam.submittedAt} />
      ) : !exam.attemptId ? (
        <SessionEntryPanel
          sessionCode={exam.sessionCode}
          isBusy={exam.isBusy}
          error={exam.error}
          onSessionCodeChange={exam.setSessionCode}
          onStartAttempt={exam.startAttempt}
        />
      ) : (
        <ExamTakingPanel
          blocks={exam.blocks}
          answers={exam.answers}
          missingRequired={exam.missingRequired}
          isBusy={exam.isBusy}
          status={exam.status}
          error={exam.error}
          onAnswerChange={exam.setAnswer}
          onSave={exam.saveAnswers}
          onSubmit={exam.submitAttempt}
        />
      )}
    </StudentShell>
  );
}
