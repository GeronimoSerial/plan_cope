CREATE TABLE IF NOT EXISTS attempt_resume_credentials (
    attempt_id TEXT PRIMARY KEY,
    delivery_session_id TEXT NOT NULL,
    token_hash TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (attempt_id) REFERENCES student_attempts(id) ON DELETE CASCADE,
    FOREIGN KEY (delivery_session_id) REFERENCES delivery_sessions(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_attempt_resume_credentials_expiry
    ON attempt_resume_credentials (expires_at);

ALTER TABLE submission_answers ADD COLUMN revision INTEGER NOT NULL DEFAULT 0;
