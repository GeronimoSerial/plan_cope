CREATE TABLE IF NOT EXISTS attempt_resume_credential_revocations (
    delivery_session_id TEXT NOT NULL,
    token_hash TEXT NOT NULL,
    attempt_id TEXT NULL,
    revoked_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    PRIMARY KEY (delivery_session_id, token_hash),
    FOREIGN KEY (delivery_session_id) REFERENCES delivery_sessions(id) ON DELETE CASCADE,
    FOREIGN KEY (attempt_id) REFERENCES student_attempts(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_attempt_resume_credential_revocations_expiry
    ON attempt_resume_credential_revocations (expires_at);
