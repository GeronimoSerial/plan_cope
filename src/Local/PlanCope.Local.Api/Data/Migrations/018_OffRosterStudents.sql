CREATE TABLE session_extra_students (
    id TEXT PRIMARY KEY,
    session_id TEXT NOT NULL,
    document_hmac TEXT NOT NULL,
    document_last4 TEXT NOT NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (session_id) REFERENCES delivery_sessions(id) ON DELETE CASCADE,
    UNIQUE (session_id, document_hmac)
);

CREATE INDEX ix_session_extra_students_session
    ON session_extra_students (session_id, last_name, first_name);

ALTER TABLE student_attempts ADD COLUMN extra_student_id TEXT NULL;
CREATE UNIQUE INDEX ux_student_attempts_extra_student
    ON student_attempts (delivery_session_id, extra_student_id)
    WHERE extra_student_id IS NOT NULL;

ALTER TABLE student_resolutions RENAME TO student_resolutions_old;
CREATE TABLE student_resolutions (
    id TEXT PRIMARY KEY,
    delivery_session_id TEXT NOT NULL,
    roster_snapshot_id TEXT NULL,
    roster_section_id TEXT NULL,
    roster_student_id TEXT NULL,
    ge_person_id INTEGER NULL,
    extra_student_id TEXT NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    document_last4 TEXT NOT NULL,
    token_hash TEXT NOT NULL UNIQUE,
    expires_at TEXT NOT NULL,
    used_at TEXT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (delivery_session_id) REFERENCES delivery_sessions(id) ON DELETE CASCADE,
    FOREIGN KEY (extra_student_id) REFERENCES session_extra_students(id) ON DELETE CASCADE
);

INSERT INTO student_resolutions
    (id, delivery_session_id, roster_snapshot_id, roster_section_id, roster_student_id, ge_person_id,
     extra_student_id, first_name, last_name, document_last4, token_hash, expires_at, used_at, created_at)
SELECT id, delivery_session_id, roster_snapshot_id, roster_section_id, roster_student_id, ge_person_id,
       NULL, first_name, last_name, document_last4, token_hash, expires_at, used_at, created_at
FROM student_resolutions_old;
DROP TABLE student_resolutions_old;

CREATE INDEX ix_student_resolutions_session
    ON student_resolutions (delivery_session_id, expires_at);
