export type ApiErrorPayload = {
  error?: string;
  errors?: Record<string, string[]>;
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

export type LocalExamBlock = {
  id: string;
  localExamVersionId: string;
  remoteBlockId: string;
  orderIndex: number;
  blockType: number | string;
  configJson: string;
  validationJson?: string | null;
};
