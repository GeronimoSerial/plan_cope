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
  studentFirstName?: string | null;
  studentLastName?: string | null;
  documentLast4?: string | null;
};

export type ResolvedStudent = {
  displayName: string;
  maskedDocument: string;
  firstName: string;
  lastName: string;
};

export type ResolveStudentResponse = {
  resolutionToken: string;
  student: ResolvedStudent;
  expiresAt: string;
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
