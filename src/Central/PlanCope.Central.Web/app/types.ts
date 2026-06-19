export type BlockType = "Text" | "Image" | "MultipleChoice" | "TrueFalse" | "ShortAnswer";

export interface ExamSummary {
  id: string;
  code: string;
  title: string;
  level?: string | null;
  area?: string | null;
  subject?: string | null;
  status: string;
  versionCount: number;
}

export interface ExamVersion {
  id: string;
  examId: string;
  versionNumber: number;
  schemaVersion: number;
  status: string;
  metadata?: Record<string, unknown> | null;
  blocks: ExamBlock[];
  answerKeys: AnswerKey[];
  assets: ExamAsset[];
}

export interface ExamBlock {
  id: string;
  versionId: string;
  orderIndex: number;
  blockType: BlockType;
  title?: string | null;
  description?: string | null;
  config: Record<string, unknown>;
  validation?: Record<string, unknown> | null;
}

export interface AnswerKey {
  id: string;
  blockId: string;
  correctAnswer: unknown;
  scoreValue?: number | null;
  metadata?: Record<string, unknown> | null;
}

export interface ExamAsset {
  id: string;
  versionId: string;
  fileName: string;
  mimeType: string;
  sizeBytes: number;
  checksum: string;
  storagePath: string;
}

export interface PublishRequest {
  subject?: string | null;
  grade: string;
  division?: string | null;
}
