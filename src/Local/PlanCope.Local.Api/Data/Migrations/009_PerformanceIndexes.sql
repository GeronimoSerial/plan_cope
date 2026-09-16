CREATE INDEX IF NOT EXISTS ix_student_attempts_session_local_sequence
    ON student_attempts (delivery_session_id, local_sequence);