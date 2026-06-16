CREATE INDEX IF NOT EXISTS ix_local_exam_blocks_version_order
    ON local_exam_blocks (local_exam_version_id, order_index);

CREATE INDEX IF NOT EXISTS ix_local_answer_keys_version
    ON local_answer_keys (local_exam_version_id);

CREATE INDEX IF NOT EXISTS ix_local_assets_version
    ON local_assets (local_exam_version_id);

CREATE INDEX IF NOT EXISTS ix_delivery_sessions_status
    ON delivery_sessions (status);

CREATE INDEX IF NOT EXISTS ix_student_attempts_session_status
    ON student_attempts (delivery_session_id, status);

CREATE INDEX IF NOT EXISTS ix_submission_answers_attempt
    ON submission_answers (student_attempt_id);

CREATE INDEX IF NOT EXISTS ix_sync_outbox_status_next_retry
    ON sync_outbox (status, next_retry_at);

CREATE INDEX IF NOT EXISTS ix_sync_attempts_started
    ON sync_attempts (started_at);

CREATE INDEX IF NOT EXISTS ix_local_audit_logs_created
    ON local_audit_logs (created_at);
