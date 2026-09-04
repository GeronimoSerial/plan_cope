CREATE TABLE IF NOT EXISTS local_roster_snapshots (
    id TEXT PRIMARY KEY NOT NULL,
    cue TEXT NOT NULL,
    school_year TEXT NOT NULL,
    fetched_at TEXT NOT NULL,
    checksum TEXT NOT NULL,
    section_count INTEGER NOT NULL,
    student_count INTEGER NOT NULL,
    status TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_local_roster_snapshots_cue_year_checksum
    ON local_roster_snapshots (cue, school_year, checksum);

CREATE INDEX IF NOT EXISTS ix_local_roster_snapshots_cue_year_fetched
    ON local_roster_snapshots (cue, school_year, fetched_at);

CREATE TABLE IF NOT EXISTS local_roster_sections (
    id TEXT PRIMARY KEY NOT NULL,
    snapshot_id TEXT NOT NULL,
    ge_section_id INTEGER NULL,
    course TEXT NULL,
    division TEXT NULL,
    level TEXT NULL,
    shift TEXT NULL,
    FOREIGN KEY (snapshot_id) REFERENCES local_roster_snapshots (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_local_roster_sections_snapshot
    ON local_roster_sections (snapshot_id);

CREATE TABLE IF NOT EXISTS local_roster_students (
    id TEXT PRIMARY KEY NOT NULL,
    snapshot_id TEXT NOT NULL,
    section_id TEXT NOT NULL,
    ge_person_id INTEGER NOT NULL,
    document_hash TEXT NOT NULL,
    document_last4 TEXT NOT NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    FOREIGN KEY (snapshot_id) REFERENCES local_roster_snapshots (id) ON DELETE CASCADE,
    FOREIGN KEY (section_id) REFERENCES local_roster_sections (id) ON DELETE CASCADE,
    UNIQUE (snapshot_id, section_id, ge_person_id)
);

CREATE INDEX IF NOT EXISTS ix_local_roster_students_snapshot_section_document_hash
    ON local_roster_students (snapshot_id, section_id, document_hash);
