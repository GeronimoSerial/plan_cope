ALTER TABLE delivery_sessions ADD COLUMN access_code TEXT NULL;
ALTER TABLE delivery_sessions ADD COLUMN expected_student_count INTEGER NOT NULL DEFAULT 1;

UPDATE delivery_sessions
SET access_code = substr(replace(id, '-', ''), 1, 6)
WHERE access_code IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_delivery_sessions_access_code
    ON delivery_sessions (access_code);
