import { z } from 'zod';

// ── Exams ──

export const examStatusEnum = z.enum(['active', 'inactive', 'archived']);

export const examInsert = z.object({
  code: z.string().min(1),
  title: z.string().min(1),
  description: z.string().nullable().optional(),
  level: z.string().nullable().optional(),
  area: z.string().nullable().optional(),
  subject: z.string().nullable().optional(),
  status: examStatusEnum.optional(),
  deleted_at: z.string().datetime().nullable().optional(),
});

export const examSelect = z.object({
  id: z.string().uuid(),
  code: z.string(),
  title: z.string(),
  description: z.string().nullable(),
  level: z.string().nullable(),
  area: z.string().nullable(),
  subject: z.string().nullable(),
  status: examStatusEnum,
  deleted_at: z.string().datetime().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type ExamInsert = z.infer<typeof examInsert>;
export type ExamSelect = z.infer<typeof examSelect>;

// ── Exam Versions ──

export const examVersionStatusEnum = z.enum(['draft', 'review', 'approved', 'published', 'archived']);

export const examVersionInsert = z.object({
  exam_id: z.string().uuid(),
  version_number: z.number().int().positive(),
  schema_version: z.number().int().positive().optional(),
  status: examVersionStatusEnum.optional(),
  metadata_json: z.any().nullable().optional(),
  created_by: z.string().uuid().nullable().optional(),
  reviewed_by: z.string().uuid().nullable().optional(),
  approved_by: z.string().uuid().nullable().optional(),
  published_by: z.string().uuid().nullable().optional(),
  published_at: z.string().datetime().nullable().optional(),
});

export const examVersionSelect = z.object({
  id: z.string().uuid(),
  exam_id: z.string().uuid(),
  version_number: z.number().int().positive(),
  schema_version: z.number().int().positive(),
  status: examVersionStatusEnum,
  metadata_json: z.any().nullable(),
  created_by: z.string().uuid().nullable(),
  reviewed_by: z.string().uuid().nullable(),
  approved_by: z.string().uuid().nullable(),
  published_by: z.string().uuid().nullable(),
  published_at: z.string().datetime().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type ExamVersionInsert = z.infer<typeof examVersionInsert>;
export type ExamVersionSelect = z.infer<typeof examVersionSelect>;

// ── Exam Blocks (discriminated union on block_type) ──

export const blockTypeEnum = z.enum(['text', 'image', 'multiple_choice', 'true_false', 'short_answer']);

const textConfig = z.object({ text: z.string() });
const imageConfig = z.object({ asset_id: z.string(), alt: z.string().optional(), caption: z.string().optional() });
const multipleChoiceConfig = z.object({ question: z.string(), options: z.array(z.string()) });
const trueFalseConfig = z.object({ statement: z.string() });
const shortAnswerConfig = z.object({
  question: z.string(),
  max_length: z.number().int().positive().optional(),
  case_sensitive: z.boolean().optional(),
  trim: z.boolean().optional(),
});

const textBlockInsert = z.object({
  exam_version_id: z.string().uuid(),
  order_index: z.number().int().nonnegative(),
  block_type: z.literal('text'),
  title: z.string().nullable().optional(),
  description: z.string().nullable().optional(),
  config_json: textConfig,
  validation_json: z.any().nullable().optional(),
});

const imageBlockInsert = z.object({
  exam_version_id: z.string().uuid(),
  order_index: z.number().int().nonnegative(),
  block_type: z.literal('image'),
  title: z.string().nullable().optional(),
  description: z.string().nullable().optional(),
  config_json: imageConfig,
  validation_json: z.any().nullable().optional(),
});

const multipleChoiceBlockInsert = z.object({
  exam_version_id: z.string().uuid(),
  order_index: z.number().int().nonnegative(),
  block_type: z.literal('multiple_choice'),
  title: z.string().nullable().optional(),
  description: z.string().nullable().optional(),
  config_json: multipleChoiceConfig,
  validation_json: z.any().nullable().optional(),
});

const trueFalseBlockInsert = z.object({
  exam_version_id: z.string().uuid(),
  order_index: z.number().int().nonnegative(),
  block_type: z.literal('true_false'),
  title: z.string().nullable().optional(),
  description: z.string().nullable().optional(),
  config_json: trueFalseConfig,
  validation_json: z.any().nullable().optional(),
});

const shortAnswerBlockInsert = z.object({
  exam_version_id: z.string().uuid(),
  order_index: z.number().int().nonnegative(),
  block_type: z.literal('short_answer'),
  title: z.string().nullable().optional(),
  description: z.string().nullable().optional(),
  config_json: shortAnswerConfig,
  validation_json: z.any().nullable().optional(),
});

export const examBlockInsert = z.discriminatedUnion('block_type', [
  textBlockInsert,
  imageBlockInsert,
  multipleChoiceBlockInsert,
  trueFalseBlockInsert,
  shortAnswerBlockInsert,
]);

export const examBlockSelect = z.object({
  id: z.string().uuid(),
  exam_version_id: z.string().uuid(),
  order_index: z.number().int().nonnegative(),
  block_type: blockTypeEnum,
  title: z.string().nullable(),
  description: z.string().nullable(),
  config_json: z.any(),
  validation_json: z.any().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type ExamBlockInsert = z.infer<typeof examBlockInsert>;
export type ExamBlockSelect = z.infer<typeof examBlockSelect>;

// ── Exam Block Options ──

export const examBlockOptionInsert = z.object({
  exam_block_id: z.string().uuid(),
  value: z.string().min(1),
  label: z.string().min(1),
  order_index: z.number().int().nonnegative().optional(),
  metadata_json: z.any().nullable().optional(),
});

export const examBlockOptionSelect = z.object({
  id: z.string().uuid(),
  exam_block_id: z.string().uuid(),
  value: z.string(),
  label: z.string(),
  order_index: z.number().int().nonnegative(),
  metadata_json: z.any().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type ExamBlockOptionInsert = z.infer<typeof examBlockOptionInsert>;
export type ExamBlockOptionSelect = z.infer<typeof examBlockOptionSelect>;

// ── Answer Keys ──

export const answerKeyInsert = z.object({
  exam_block_id: z.string().uuid(),
  correct_answer_json: z.any(),
  score_value: z.number().nullable().optional(),
  metadata_json: z.any().nullable().optional(),
});

export const answerKeySelect = z.object({
  id: z.string().uuid(),
  exam_block_id: z.string().uuid(),
  correct_answer_json: z.any(),
  score_value: z.number().nullable(),
  metadata_json: z.any().nullable(),
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type AnswerKeyInsert = z.infer<typeof answerKeyInsert>;
export type AnswerKeySelect = z.infer<typeof answerKeySelect>;

// ── Exam Assets ──

export const examAssetInsert = z.object({
  exam_version_id: z.string().uuid(),
  file_name: z.string().min(1),
  mime_type: z.string().min(1),
  size_bytes: z.number().int().positive(),
  checksum: z.string().min(1),
  storage_path: z.string().min(1),
});

export const examAssetSelect = z.object({
  id: z.string().uuid(),
  exam_version_id: z.string().uuid(),
  file_name: z.string(),
  mime_type: z.string(),
  size_bytes: z.number().int().positive(),
  checksum: z.string(),
  storage_path: z.string(),
  created_at: z.string().datetime(),
});

export type ExamAssetInsert = z.infer<typeof examAssetInsert>;
export type ExamAssetSelect = z.infer<typeof examAssetSelect>;

// ── Asset Usages ──

export const usageTypeEnum = z.enum(['prompt', 'option_image', 'reference', 'attachment']);

export const assetUsageInsert = z.object({
  exam_block_id: z.string().uuid(),
  exam_asset_id: z.string().uuid(),
  usage_type: usageTypeEnum,
});

export const assetUsageSelect = z.object({
  id: z.string().uuid(),
  exam_block_id: z.string().uuid(),
  exam_asset_id: z.string().uuid(),
  usage_type: usageTypeEnum,
  created_at: z.string().datetime(),
  updated_at: z.string().datetime(),
});

export type AssetUsageInsert = z.infer<typeof assetUsageInsert>;
export type AssetUsageSelect = z.infer<typeof assetUsageSelect>;
