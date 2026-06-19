// Tipos alineados con PlanCope.Shared.Contracts (backend .NET).
// El enum BlockType viaja como string una vez registrado JsonStringEnumConverter en el API;
// el mapper (schema/mappers.ts) normaliza tambien valores numericos por compatibilidad.

export const blockTypes = ["Text", "Image", "MultipleChoice", "TrueFalse", "ShortAnswer"] as const;

export type BlockType = (typeof blockTypes)[number];

export interface LoginRequest {
  username: string;
  password: string;
}

export interface UserProfile {
  id: string;
  displayName: string;
  role: string;
  schoolId?: string | null;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken?: string | null;
  user: UserProfile;
}

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

export interface CreateExamRequest {
  code: string;
  title: string;
  description?: string | null;
  level?: string | null;
  area?: string | null;
  subject?: string | null;
}

export interface CreateExamVersionRequest {
  schemaVersion: number;
  metadata?: Record<string, unknown> | null;
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
  blockType: BlockType | number;
  title?: string | null;
  description?: string | null;
  config: Record<string, unknown>;
  validation?: Record<string, unknown> | null;
}

export interface UpsertBlockRequest {
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

// Nuevo contrato canonico (endpoint aditivo PUT /api/exams/versions/{id}/document).
export interface ReplaceExamDocumentRequest {
  metadata?: Record<string, unknown> | null;
  blocks: DocumentBlock[];
}

export interface DocumentBlock {
  orderIndex: number;
  blockType: BlockType;
  title?: string | null;
  description?: string | null;
  config: Record<string, unknown>;
  validation?: Record<string, unknown> | null;
  correctAnswer?: unknown;
  scoreValue?: number | null;
}

export interface PublishExamVersionRequest {
  subject?: string | null;
  grade: string;
  division?: string | null;
}

export interface PublicationTarget {
  targetType: string;
  targetId?: string | null;
}

export interface PublishExamVersionResponse {
  packageId: string;
  examVersionId: string;
  packageVersion: number;
  checksum: string;
  targets?: PublicationTarget[];
}
