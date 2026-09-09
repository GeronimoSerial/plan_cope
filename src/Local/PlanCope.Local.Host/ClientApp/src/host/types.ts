import type { LocalExam } from "../shared/api-types";

export type HostContext = {
  apiBaseUrl: string;
  lanBaseUrl: string;
  operatorName: string;
  port: number;
  isActivated: boolean;
};

export type HostContextMessage = {
  type: "host:context";
  context: HostContext;
};

export type NativeBridge = {
  postMessage: (message: unknown) => void;
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
  schoolYear?: string | null;
  rosterSnapshotId?: string | null;
  rosterSectionId?: string | null;
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
  schoolYear?: string | null;
  rosterSnapshotId?: string | null;
  rosterSectionId?: string | null;
};

export type RosterSnapshot = {
  id: string;
  cue: string;
  schoolYear: string;
  fetchedAt: string;
  checksum: string;
  sectionCount: number;
  studentCount: number;
  status: string;
  schoolName?: string | null;
};

export type RosterSection = {
  id: string;
  snapshotId: string;
  geSectionId?: number | null;
  course?: string | null;
  division?: string | null;
  level?: string | null;
  shift?: string | null;
  studentCount: number;
};

export type RosterResponse = {
  snapshot: RosterSnapshot | null;
  sections: RosterSection[];
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

export type FormErrors = {
  cue?: string;
  classroomCode?: string;
  expectedStudentCount?: string;
  operatorName?: string;
  selectedExamId?: string;
  accessCode?: string;
  rosterSectionId?: string;
};
