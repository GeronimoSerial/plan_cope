ALTER TABLE delivery_sessions ADD COLUMN school_year TEXT NULL;
ALTER TABLE delivery_sessions ADD COLUMN roster_snapshot_id TEXT NULL;
ALTER TABLE delivery_sessions ADD COLUMN roster_section_id TEXT NULL;

CREATE INDEX IF NOT EXISTS ix_delivery_sessions_roster_snapshot
    ON delivery_sessions (roster_snapshot_id);

CREATE INDEX IF NOT EXISTS ix_delivery_sessions_roster_section
    ON delivery_sessions (roster_section_id);
