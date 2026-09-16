CREATE TABLE IF NOT EXISTS node_identity (
    id TEXT PRIMARY KEY NOT NULL,
    node_id TEXT NULL,
    cue TEXT NOT NULL REFERENCES schools (cue),
    fingerprint_hash TEXT NOT NULL,
    fingerprint_components_json TEXT NOT NULL,
    enrolled_at TEXT NULL,
    last_sync_at TEXT NULL,
    credential_state TEXT NOT NULL,
    revocation_detected_at TEXT NULL,
    revocation_stage TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_node_identity_cue ON node_identity (cue);