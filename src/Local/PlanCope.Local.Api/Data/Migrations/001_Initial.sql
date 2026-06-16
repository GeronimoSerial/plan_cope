CREATE TABLE IF NOT EXISTS local_users (
    id TEXT PRIMARY KEY,
    username TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    role TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1,
    last_login_at TEXT NULL,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS local_exam_versions (
    id TEXT PRIMARY KEY,
    remote_exam_version_id TEXT NOT NULL UNIQUE,
    exam_code TEXT NOT NULL,
    version_number INTEGER NOT NULL,
    checksum TEXT NOT NULL,
    metadata_json TEXT NULL,
    schema_version INTEGER NOT NULL,
    synced_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS local_exam_blocks (
    id TEXT PRIMARY KEY,
    local_exam_version_id TEXT NOT NULL,
    remote_block_id TEXT NOT NULL UNIQUE,
    order_index INTEGER NOT NULL,
    block_type TEXT NOT NULL,
    config_json TEXT NOT NULL,
    validation_json TEXT NULL,
    FOREIGN KEY (local_exam_version_id) REFERENCES local_exam_versions(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS local_answer_keys (
    id TEXT PRIMARY KEY,
    local_exam_version_id TEXT NOT NULL,
    remote_block_id TEXT NOT NULL,
    correct_answer_json TEXT NOT NULL,
    score_value REAL NULL,
    FOREIGN KEY (local_exam_version_id) REFERENCES local_exam_versions(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS local_assets (
    id TEXT PRIMARY KEY,
    remote_asset_id TEXT NOT NULL UNIQUE,
    local_exam_version_id TEXT NOT NULL,
    file_name TEXT NOT NULL,
    mime_type TEXT NOT NULL,
    checksum TEXT NOT NULL,
    local_path TEXT NOT NULL,
    synced_at TEXT NOT NULL,
    FOREIGN KEY (local_exam_version_id) REFERENCES local_exam_versions(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS delivery_sessions (
    id TEXT PRIMARY KEY,
    exam_version_id TEXT NOT NULL,
    school_code TEXT NOT NULL,
    classroom_code TEXT NULL,
    commission_code TEXT NULL,
    started_by TEXT NOT NULL,
    start_at TEXT NOT NULL,
    end_at TEXT NULL,
    status TEXT NOT NULL,
    config_json TEXT NULL,
    FOREIGN KEY (exam_version_id) REFERENCES local_exam_versions(id)
);

CREATE TABLE IF NOT EXISTS student_attempts (
    id TEXT PRIMARY KEY,
    delivery_session_id TEXT NOT NULL,
    student_code TEXT NOT NULL,
    status TEXT NOT NULL,
    started_at TEXT NOT NULL,
    submitted_at TEXT NULL,
    local_sequence INTEGER NOT NULL,
    confirmation_code TEXT NULL,
    FOREIGN KEY (delivery_session_id) REFERENCES delivery_sessions(id) ON DELETE CASCADE,
    UNIQUE (delivery_session_id, student_code)
);

CREATE TABLE IF NOT EXISTS submission_answers (
    id TEXT PRIMARY KEY,
    student_attempt_id TEXT NOT NULL,
    block_id TEXT NOT NULL,
    answer_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    FOREIGN KEY (student_attempt_id) REFERENCES student_attempts(id) ON DELETE CASCADE,
    FOREIGN KEY (block_id) REFERENCES local_exam_blocks(id),
    UNIQUE (student_attempt_id, block_id)
);

CREATE TABLE IF NOT EXISTS sync_outbox (
    id TEXT PRIMARY KEY,
    event_type TEXT NOT NULL,
    aggregate_type TEXT NOT NULL,
    aggregate_id TEXT NOT NULL,
    idempotency_key TEXT NOT NULL UNIQUE,
    payload_json TEXT NOT NULL,
    status TEXT NOT NULL,
    retry_count INTEGER NOT NULL DEFAULT 0,
    next_retry_at TEXT NULL,
    last_error TEXT NULL,
    created_at TEXT NOT NULL,
    processed_at TEXT NULL
);

CREATE TABLE IF NOT EXISTS sync_state (
    id TEXT PRIMARY KEY,
    key TEXT NOT NULL UNIQUE,
    value_json TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sync_attempts (
    id TEXT PRIMARY KEY,
    direction TEXT NOT NULL,
    status TEXT NOT NULL,
    started_at TEXT NOT NULL,
    finished_at TEXT NULL,
    summary_json TEXT NULL,
    error_json TEXT NULL
);

CREATE TABLE IF NOT EXISTS local_audit_logs (
    id TEXT PRIMARY KEY,
    actor_id TEXT NULL,
    action TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    payload_json TEXT NULL,
    created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app_settings (
    id TEXT PRIMARY KEY,
    key TEXT NOT NULL UNIQUE,
    value_json TEXT NOT NULL,
    updated_at TEXT NOT NULL
);
