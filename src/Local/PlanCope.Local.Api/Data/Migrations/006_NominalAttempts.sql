ALTER TABLE student_attempts ADD COLUMN roster_student_id TEXT NULL;
ALTER TABLE student_attempts ADD COLUMN ge_person_id INTEGER NULL;
ALTER TABLE student_attempts ADD COLUMN student_first_name TEXT NULL;
ALTER TABLE student_attempts ADD COLUMN student_last_name TEXT NULL;
ALTER TABLE student_attempts ADD COLUMN document_last4 TEXT NULL;
ALTER TABLE student_attempts ADD COLUMN verification_source TEXT NULL;
ALTER TABLE student_attempts ADD COLUMN verified_at TEXT NULL;

CREATE TABLE IF NOT EXISTS student_resolutions (
    id TEXT PRIMARY KEY,
    delivery_session_id TEXT NOT NULL,
    roster_snapshot_id TEXT NOT NULL,
    roster_section_id TEXT NOT NULL,
    roster_student_id TEXT NOT NULL,
    ge_person_id INTEGER NOT NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    document_last4 TEXT NOT NULL,
    token_hash TEXT NOT NULL UNIQUE,
    expires_at TEXT NOT NULL,
    used_at TEXT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (delivery_session_id) REFERENCES delivery_sessions(id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_student_resolutions_session
    ON student_resolutions (delivery_session_id, expires_at);

CREATE UNIQUE INDEX IF NOT EXISTS ux_student_attempts_nominal_student
    ON student_attempts (delivery_session_id, ge_person_id)
    WHERE ge_person_id IS NOT NULL;
