CREATE INDEX IF NOT EXISTS ix_delivery_sessions_school_status_start
    ON delivery_sessions (school_code, status, start_at DESC);
