CREATE TABLE IF NOT EXISTS schools (
    cue TEXT PRIMARY KEY NOT NULL,
    name TEXT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

INSERT OR IGNORE INTO schools (cue, name, created_at)
SELECT DISTINCT cue, school_name, datetime('now')
FROM local_roster_snapshots
WHERE cue IS NOT NULL;

INSERT OR IGNORE INTO schools (cue, name, created_at)
SELECT DISTINCT school_code, NULL, datetime('now')
FROM delivery_sessions
WHERE school_code IS NOT NULL;

INSERT INTO local_audit_logs (id, actor_id, action, entity_type, entity_id, payload_json, created_at)
SELECT lower(hex(randomblob(16))), NULL, 'schools_backfill_stub', 'schools', school_code, NULL, datetime('now')
FROM (
    SELECT DISTINCT school_code
    FROM delivery_sessions
    WHERE school_code IS NOT NULL
      AND school_code NOT IN (SELECT cue FROM local_roster_snapshots WHERE cue IS NOT NULL)
);

PRAGMA foreign_keys=OFF;

CREATE TABLE local_roster_snapshots_new (
    id TEXT PRIMARY KEY NOT NULL,
    cue TEXT NOT NULL,
    school_year TEXT NOT NULL,
    fetched_at TEXT NOT NULL,
    checksum TEXT NOT NULL,
    section_count INTEGER NOT NULL,
    student_count INTEGER NOT NULL,
    status TEXT NOT NULL,
    school_name TEXT NULL,
    FOREIGN KEY (cue) REFERENCES schools (cue)
);

INSERT INTO local_roster_snapshots_new (id, cue, school_year, fetched_at, checksum, section_count, student_count, status, school_name)
SELECT id, cue, school_year, fetched_at, checksum, section_count, student_count, status, school_name
FROM local_roster_snapshots;

DROP TABLE local_roster_snapshots;
ALTER TABLE local_roster_snapshots_new RENAME TO local_roster_snapshots;

CREATE UNIQUE INDEX IF NOT EXISTS ux_local_roster_snapshots_cue_year_checksum
    ON local_roster_snapshots (cue, school_year, checksum);

CREATE INDEX IF NOT EXISTS ix_local_roster_snapshots_cue_year_fetched
    ON local_roster_snapshots (cue, school_year, fetched_at);

CREATE TABLE delivery_sessions_new (
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
    access_code TEXT NULL,
    expected_student_count INTEGER NOT NULL DEFAULT 1,
    school_year TEXT NULL,
    roster_snapshot_id TEXT NULL,
    roster_section_id TEXT NULL,
    FOREIGN KEY (exam_version_id) REFERENCES local_exam_versions (id),
    FOREIGN KEY (school_code) REFERENCES schools (cue),
    FOREIGN KEY (roster_snapshot_id) REFERENCES local_roster_snapshots (id),
    FOREIGN KEY (roster_section_id) REFERENCES local_roster_sections (id)
);

INSERT INTO delivery_sessions_new (id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count, school_year, roster_snapshot_id, roster_section_id)
SELECT id, exam_version_id, school_code, classroom_code, commission_code, started_by, start_at, end_at, status, config_json, access_code, expected_student_count, school_year, roster_snapshot_id, roster_section_id
FROM delivery_sessions;

DROP TABLE delivery_sessions;
ALTER TABLE delivery_sessions_new RENAME TO delivery_sessions;

CREATE UNIQUE INDEX IF NOT EXISTS ux_delivery_sessions_access_code
    ON delivery_sessions (access_code);

CREATE INDEX IF NOT EXISTS ix_delivery_sessions_status
    ON delivery_sessions (status);

CREATE INDEX IF NOT EXISTS ix_delivery_sessions_roster_snapshot
    ON delivery_sessions (roster_snapshot_id);

CREATE INDEX IF NOT EXISTS ix_delivery_sessions_roster_section
    ON delivery_sessions (roster_section_id);

PRAGMA foreign_keys=ON;
