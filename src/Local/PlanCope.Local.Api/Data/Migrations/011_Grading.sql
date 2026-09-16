CREATE TABLE IF NOT EXISTS attempt_results (
    id TEXT PRIMARY KEY,
    student_attempt_id TEXT NOT NULL,
    grading_schema_version INTEGER NOT NULL,
    scoring_policy TEXT NULL,
    status TEXT NOT NULL,
    score REAL NULL,
    score_max REAL NULL,
    blocks_json TEXT NULL,
    graded_at TEXT NOT NULL,
    FOREIGN KEY (student_attempt_id) REFERENCES student_attempts(id) ON DELETE CASCADE,
    UNIQUE (student_attempt_id, grading_schema_version)
);