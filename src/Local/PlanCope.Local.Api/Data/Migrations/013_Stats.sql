CREATE TABLE IF NOT EXISTS stats_rollups (
    id TEXT PRIMARY KEY,
    cue TEXT NOT NULL REFERENCES schools(cue),
    school_year TEXT NOT NULL,
    course TEXT NOT NULL,
    exam_version_id TEXT NOT NULL REFERENCES local_exam_versions(id),
    attempt_count INTEGER NOT NULL DEFAULT 0,
    score_sum REAL NOT NULL DEFAULT 0,
    score_max_sum REAL NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL,
    UNIQUE (cue, school_year, course, exam_version_id)
);

CREATE TABLE IF NOT EXISTS stats_rollup_blocks (
    id TEXT PRIMARY KEY,
    rollup_id TEXT NOT NULL REFERENCES stats_rollups(id) ON DELETE CASCADE,
    block_id TEXT NOT NULL,
    correct_count INTEGER NOT NULL DEFAULT 0,
    partial_count INTEGER NOT NULL DEFAULT 0,
    incorrect_count INTEGER NOT NULL DEFAULT 0,
    blank_count INTEGER NOT NULL DEFAULT 0,
    ungradable_count INTEGER NOT NULL DEFAULT 0,
    score_sum REAL NOT NULL DEFAULT 0,
    score_max_sum REAL NOT NULL DEFAULT 0,
    UNIQUE (rollup_id, block_id)
);