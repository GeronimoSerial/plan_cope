ALTER TABLE attempt_resume_credentials ADD COLUMN revoked_at TEXT NULL;

CREATE TABLE IF NOT EXISTS attempt_resume_proofs (
    attempt_id TEXT PRIMARY KEY,
    delivery_session_id TEXT NOT NULL,
    proof_hash TEXT NOT NULL,
    revoked_at TEXT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (attempt_id) REFERENCES student_attempts(id) ON DELETE CASCADE,
    FOREIGN KEY (delivery_session_id) REFERENCES delivery_sessions(id) ON DELETE CASCADE
);
