import type { LocalExamBlock } from "../shared/api-types";

export type StudentAttempt = {
  id: string;
  deliverySessionId: string;
  studentCode: string;
  status: string;
  startedAt: string;
  submittedAt?: string | null;
  localSequence: number;
  confirmationCode?: string | null;
};

export type StartAttemptResponse = {
  attempt: StudentAttempt;
  blocks: LocalExamBlock[];
};

export type SubmitAttemptResponse = {
  attemptId: string;
  confirmationCode: string;
  submittedAt: string;
};
