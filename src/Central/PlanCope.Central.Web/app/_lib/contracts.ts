// Types aligned with PlanCope.Shared.Contracts (.NET backend).
// BlockType is serialized as a string after the API registers JsonStringEnumConverter;
// schema/mappers.ts also normalizes numeric values for compatibility.

export const blockTypes = ["MultipleChoice", "TrueFalse"] as const;

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
  rosterScope: string;
  rosterCues: string[];
}

export interface LoginResponse {
  accessToken: string;
  refreshToken?: string | null;
  user: UserProfile;
}

export type PublicationState = "draft" | "ready_to_publish" | "published";

export interface ExamSummary {
  id: string;
  code: string;
  title: string;
  level?: string | null;
  area?: string | null;
  subject?: string | null;
  status: string;
  versionCount: number;
  initialVersionId?: string | null;
  publicationState?: PublicationState;
  publishedVersionId?: string | null;
  publishedVersionNumber?: number | null;
  publishedAt?: string | null;
  targets?: PublicationTarget[] | null;
  pulledByNodeCount?: number | null;
}

export interface CreateExamRequest {
  code: string;
  title: string;
  description?: string | null;
  level?: string | null;
  area?: string | null;
  subject?: string | null;
}

// Create versions. Every field is optional; by default, the API deep-copies the
// source version (`sourceVersionId`, or the latest when omitted). `empty: true` keeps the
// previous behavior of creating an empty version.
export interface CreateExamVersionRequest {
  schemaVersion?: number;
  metadata?: Record<string, unknown> | null;
  sourceVersionId?: string;
  empty?: boolean;
}

// Edit exam data. `code` is immutable: when it differs from the stored value, the API
// returns 400 with a ValidationProblem under the "code" key.
export interface UpdateExamRequest {
  title: string;
  description?: string | null;
  level?: string | null;
  area?: string | null;
  subject?: string | null;
  code?: string;
}

export type PublishBlockedReason = "already_published" | "no_blocks";

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
  blockCount: number;
  canPublish: boolean;
  publishBlockedReason?: PublishBlockedReason | null;
  /** Fecha de publicacion de esta version (null si sigue en borrador). */
  publishedAt?: string | null;
  /** Fecha en que una version publicada posterior la reemplazo (null si no fue reemplazada). */
  supersededAt?: string | null;
  /** true solo para la ultima version publicada del examen. */
  isCurrent: boolean;
  /** Numero de la version de la que se copio esta (null para versiones vacias/iniciales). */
  basedOnVersionNumber?: number | null;
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

// New canonical contract (additive PUT /api/exams/versions/{id}/document endpoint).
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
  nodeIds?: string[];
  schoolIds?: string[];
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

// Desktop installer published in the private repository (B7.T15). downloadUrl is a
// private, authenticated URL (requires the storage token); the browser cannot authenticate
// with GitHub, so public downloads would require the API to act as a
// proxy (out of scope).
export interface InstallerReference {
  version: string;
  channel: string;
  downloadUrl: string;
  sha256: string;
  publishedAt: string;
}
