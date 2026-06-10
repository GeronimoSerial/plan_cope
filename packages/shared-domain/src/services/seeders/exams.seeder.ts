/**
 * Exams seeder — central PostgreSQL.
 *
 * Inserts 3 placeholder exams (Matemática, Lengua, Ciencias Naturales),
 * each with one `exam_version` and a handful of blocks that together
 * cover all 5 block types: `text`, `image`, `multiple_choice`,
 * `true_false`, `short_answer`.
 *
 * Each `multiple_choice` block has its `exam_block_options` rows.
 * Each block (except pure `text` instructions) has a matching
 * `answer_keys` row — we verify the cross-reference in the test.
 *
 * The version checksum is computed by `computeChecksum()` from
 * `exam.service.ts` and stored in `metadata_json.checksum` (the
 * `exam_versions` table in DDL_V3 does not have a dedicated
 * `checksum` column, so the value lives inside the JSONB metadata).
 * Fase 2 sync will use this value to detect version drift between
 * central and local nodes.
 */
import { sql } from 'drizzle-orm';
import type { PostgresJsDatabase } from 'drizzle-orm/postgres-js';
import { computeChecksum } from '../exam.service.js';
import {
  answerKeys,
  examBlockOptions,
  examBlocks,
  examVersions,
  exams,
} from '../../db/central/schema.js';
import type { ExamBlockInput } from '../exam.service.js';

interface ExamSeed {
  code: string;
  title: string;
  description: string;
  subject: string;
  level: string;
  area: string;
  blocks: ReadonlyArray<ExamBlockInput>;
  /** Map from block.orderIndex → correct answer payload. */
  answers: ReadonlyArray<{
    orderIndex: number;
    correct: Record<string, unknown>;
    score: string;
  }>;
}

const examMatematica: ExamSeed = {
  code: 'EX-MAT-6-2026',
  title: 'Evaluación de Matemática - 6to Grado',
  description: 'Operativo Aprender 2026 - Matemática',
  subject: 'Matemática',
  level: 'primaria',
  area: 'Matemática',
  blocks: [
    { orderIndex: 0, blockType: 'text', config: { text: 'Lee con atención y marca la respuesta correcta. Tenés 45 minutos.' } },
    {
      orderIndex: 1,
      blockType: 'multiple_choice',
      config: { question: '¿Cuánto es 25 × 4?', options: ['100', '80', '120', '90'] },
    },
    {
      orderIndex: 2,
      blockType: 'multiple_choice',
      config: { question: '¿Qué fracción representa 0.5?', options: ['1/2', '1/4', '2/3', '5/10'] },
    },
    {
      orderIndex: 3,
      blockType: 'true_false',
      config: { statement: 'Un triángulo equilátero tiene todos sus lados iguales.' },
    },
    {
      orderIndex: 4,
      blockType: 'short_answer',
      config: {
        question: 'Explicá cómo resolviste el problema de la multiplicación.',
        max_length: 200,
      },
    },
  ],
  answers: [
    { orderIndex: 1, correct: { selected: 0 }, score: '1.00' },
    { orderIndex: 2, correct: { selected: 0 }, score: '1.00' },
    { orderIndex: 3, correct: { value: true }, score: '1.00' },
    {
      orderIndex: 4,
      correct: { text: 'Multiplicar 25 por 4 es sumar 25 cuatro veces.', case_sensitive: false, trim: true },
      score: '2.00',
    },
  ],
};

const examLengua: ExamSeed = {
  code: 'EX-LEN-6-2026',
  title: 'Evaluación de Lengua - 6to Grado',
  description: 'Operativo Aprender 2026 - Lengua',
  subject: 'Lengua',
  level: 'primaria',
  area: 'Lengua',
  blocks: [
    { orderIndex: 0, blockType: 'text', config: { text: 'Lee el siguiente texto y respondé las preguntas.' } },
    {
      orderIndex: 1,
      blockType: 'multiple_choice',
      config: { question: '¿Cuál es el sujeto en "Los pájaros cantan al amanecer"?', options: ['pájaros', 'amanecer', 'cantan', 'al'] },
    },
    {
      orderIndex: 2,
      blockType: 'multiple_choice',
      config: { question: 'Elegí la opción con ortografía correcta:', options: ['habia', 'había', 'avía', 'havia'] },
    },
    {
      orderIndex: 3,
      blockType: 'short_answer',
      config: { question: 'Escribí un sinónimo de "alegre".', max_length: 50 },
    },
  ],
  answers: [
    { orderIndex: 1, correct: { selected: 0 }, score: '1.00' },
    { orderIndex: 2, correct: { selected: 1 }, score: '1.00' },
    { orderIndex: 3, correct: { text: 'contento', case_sensitive: false, trim: true }, score: '1.00' },
  ],
};

const examCiencias: ExamSeed = {
  code: 'EX-CNN-6-2026',
  title: 'Evaluación de Ciencias Naturales - 6to Grado',
  description: 'Operativo Aprender 2026 - Ciencias Naturales',
  subject: 'Ciencias Naturales',
  level: 'primaria',
  area: 'Ciencias Naturales',
  blocks: [
    { orderIndex: 0, blockType: 'text', config: { text: 'Observá la imagen y respondé.' } },
    {
      orderIndex: 1,
      blockType: 'image',
      config: { asset_id: 'placeholder-asset-cnn-1', alt: 'Diagrama del sistema solar', caption: 'Sistema Solar' },
    },
    {
      orderIndex: 2,
      blockType: 'multiple_choice',
      config: { question: '¿Cuál es el planeta más cercano al Sol?', options: ['Tierra', 'Venus', 'Mercurio', 'Marte'] },
    },
    {
      orderIndex: 3,
      blockType: 'true_false',
      config: { statement: 'La Tierra gira alrededor del Sol.' },
    },
  ],
  answers: [
    { orderIndex: 2, correct: { selected: 2 }, score: '1.00' },
    { orderIndex: 3, correct: { value: true }, score: '1.00' },
  ],
};

const EXAMS: ReadonlyArray<ExamSeed> = [examMatematica, examLengua, examCiencias];

const findExamId = async (
  db: PostgresJsDatabase,
  code: string,
): Promise<string> => {
  const rows = await db.execute<{ id: string }>(
    sql`SELECT id FROM exams WHERE code = ${code} LIMIT 1`,
  );
  const id = rows.at(0)?.id;
  if (!id) throw new Error(`seedExams: exam "${code}" not found`);
  return id;
};

const findVersionId = async (
  db: PostgresJsDatabase,
  examId: string,
  n: number,
): Promise<string> => {
  const rows = await db.execute<{ id: string }>(
    sql`SELECT id FROM exam_versions WHERE exam_id = ${examId} AND version_number = ${n} LIMIT 1`,
  );
  const id = rows.at(0)?.id;
  if (!id) throw new Error(`seedExams: version ${n} of exam ${examId} not found`);
  return id;
};

export const seedExams = async (db: PostgresJsDatabase): Promise<void> => {
  for (const seed of EXAMS) {
    // 1. Exam (upsert by unique code).
    await db
      .insert(exams)
      .values({
        code: seed.code,
        title: seed.title,
        description: seed.description,
        level: seed.level,
        area: seed.area,
        subject: seed.subject,
        status: 'active',
      })
      .onConflictDoNothing({ target: exams.code });

    const examId = await findExamId(db, seed.code);

    // 2. Compute the version checksum up-front from the canonical block list.
    //    The DDL_V3 schema for `exam_versions` does not have a dedicated
    //    `checksum` column, so we stash it inside `metadata_json` for
    //    later retrieval by sync workers.
    const checksum = computeChecksum({
      examId,
      versionNumber: 1,
      blocks: seed.blocks.map((b) => ({
        orderIndex: b.orderIndex,
        blockType: b.blockType,
        config: b.config,
      })),
    });

    // 3. Version. (Use jsonb_build_object so the checksum is always
    //    present in the metadata_json. Cast the checksum to text so PG
    //    can infer the parameter type from the call signature.)
    await db
      .insert(examVersions)
      .values({
        exam_id: examId,
        version_number: 1,
        status: 'published',
        published_at: sql`NOW()`,
        metadata_json: sql`jsonb_build_object('source', 'seeder', 'placeholder', true, 'checksum', ${checksum}::text)`,
      })
      .onConflictDoNothing({ target: [examVersions.exam_id, examVersions.version_number] });

    const versionId = await findVersionId(db, examId, 1);

    // 4. Blocks. The `exam_blocks` table has no UNIQUE constraint on
    //    (exam_version_id, order_index), so `onConflictDoNothing()`
    //    without a target would use the PK (a new auto-generated UUID)
    //    and never trigger. We pre-check by (version_id, order_index)
    //    and skip if the block already exists.
    for (const block of seed.blocks) {
      const existingBlock = await db.execute<{ id: string }>(
        sql`SELECT id FROM exam_blocks
            WHERE exam_version_id = ${versionId} AND order_index = ${block.orderIndex} LIMIT 1`,
      );
      let blockId = existingBlock.at(0)?.id;
      if (!blockId) {
        const [inserted] = await db
          .insert(examBlocks)
          .values({
            exam_version_id: versionId,
            order_index: block.orderIndex,
            block_type: block.blockType,
            config_json: block.config,
          })
          .returning({ id: examBlocks.id });
        blockId = inserted?.id;
      }
      if (!blockId) continue;

      // Options (only for multiple_choice). Options live in
      // `config.options` per the Zod schema; extract them here.
      if (block.blockType === 'multiple_choice') {
        const cfg = block.config as { options?: unknown };
        const optionLabels = Array.isArray(cfg?.options)
          ? (cfg!.options as unknown[]).filter((o): o is string => typeof o === 'string')
          : [];
        // Idempotency: skip if this block already has the expected
        // number of options.
        const existingOpts = await db.execute<{ n: number }>(
          sql`SELECT count(*)::int AS n FROM exam_block_options WHERE exam_block_id = ${blockId}`,
        );
        if ((existingOpts.at(0)?.n ?? 0) >= optionLabels.length) continue;
        for (let oIdx = 0; oIdx < optionLabels.length; oIdx++) {
          await db.insert(examBlockOptions).values({
            exam_block_id: blockId,
            value: String(oIdx),
            label: optionLabels[oIdx]!,
            order_index: oIdx,
          });
        }
      }
    }

    // 5. Answer keys. Pre-check by (block_id); only one answer key per
    //    block is expected, so the row count check is sufficient.
    for (const ans of seed.answers) {
      const block = await db.execute<{ id: string }>(
        sql`SELECT id FROM exam_blocks
            WHERE exam_version_id = ${versionId} AND order_index = ${ans.orderIndex} LIMIT 1`,
      );
      const blockId = block.at(0)?.id;
      if (!blockId) continue;
      const existingAk = await db.execute<{ n: number }>(
        sql`SELECT count(*)::int AS n FROM answer_keys WHERE exam_block_id = ${blockId}`,
      );
      if ((existingAk.at(0)?.n ?? 0) > 0) continue;
      await db.insert(answerKeys).values({
        exam_block_id: blockId,
        correct_answer_json: ans.correct,
        score_value: ans.score,
      });
    }
  }
};
