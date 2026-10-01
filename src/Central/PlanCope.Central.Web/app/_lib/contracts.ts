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
}

export interface CreateExamRequest {
  code: string;
  title: string;
  description?: string | null;
  level?: string | null;
  area?: string | null;
  subject?: string | null;
}

// Creacion de versiones. Todos los campos son opcionales: por defecto el API deep-copia la
// version fuente (`sourceVersionId`, o la ultima si se omite). `empty: true` conserva el
// comportamiento viejo de crear una version vacia.
export interface CreateExamVersionRequest {
  schemaVersion?: number;
  metadata?: Record<string, unknown> | null;
  scoringPolicy?: string | null;
  sourceVersionId?: string;
  empty?: boolean;
}

// Edicion de los datos del examen. `code` es inmutable: si viene distinto del guardado el API
// responde 400 con un ValidationProblem bajo la clave "code".
export interface UpdateExamRequest {
  title: string;
  description?: string | null;
  level?: string | null;
  area?: string | null;
  subject?: string | null;
  code?: string;
}

export type PublishBlockedReason = "already_published" | "no_blocks" | "scoring_policy_required";

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
  scoringPolicy: string | null;
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

// Nuevo contrato canonico (endpoint aditivo PUT /api/exams/versions/{id}/document).
export interface ReplaceExamDocumentRequest {
  metadata?: Record<string, unknown> | null;
  blocks: DocumentBlock[];
  scoringPolicy: string | null;
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

// Instalador de escritorio publicado en el repo privado (B7.T15). El downloadUrl es una URL
// privada/autenticada (requiere el token del storage); el navegador no puede autenticarse
// contra GitHub, por lo que una descarga realmente publica requeriria que el API hiciera de
// proxy (fuera de alcance).
export interface InstallerReference {
  version: string;
  channel: string;
  downloadUrl: string;
  sha256: string;
  publishedAt: string;
}
