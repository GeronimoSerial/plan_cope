export type HostContext = {
  apiBaseUrl: string;
  lanBaseUrl: string;
  operatorName: string;
  port: number;
};

export type HostContextMessage = {
  type: "host:context";
  context: HostContext;
};

export type NativeBridge = {
  postMessage: (message: unknown) => void;
};

export type LocalExam = {
  id: string;
  remoteExamVersionId: string;
  examCode: string;
  versionNumber: number;
  checksum: string;
  metadataJson?: string | null;
  schemaVersion: number;
  syncedAt: string;
};

export type ExamMetadata = {
  title?: string;
  grade?: string;
  course?: string;
  curso?: string;
  division?: string;
  section?: string;
  classroom?: string;
};

export type ExamOption = LocalExam & {
  title: string;
  course?: string;
  division?: string;
  displayName: string;
};

export type CreateSessionRequest = {
  examVersionId: string;
  schoolCode: string;
  classroomCode: string;
  commissionCode: string | null;
  startedBy: string;
  expectedStudentCount: number;
  config: unknown | null;
};

export type LocalSession = {
  id: string;
  examVersionId: string;
  schoolCode: string;
  classroomCode?: string | null;
  commissionCode?: string | null;
  startedBy: string;
  startAt: string;
  endAt?: string | null;
  status: string;
  configJson?: string | null;
  accessCode: string;
  expectedStudentCount: number;
};

export type SessionProgress = {
  sessionId: string;
  accessCode: string;
  expectedStudentCount: number;
  startedCount: number;
  submittedCount: number;
  inProgressCount: number;
  completionPercentage: number;
};

export type ApiErrorPayload = {
  error?: string;
  errors?: Record<string, string[]>;
};

export type FormErrors = {
  cue?: string;
  classroomCode?: string;
  expectedStudentCount?: string;
  operatorName?: string;
  selectedExamId?: string;
  accessCode?: string;
};
