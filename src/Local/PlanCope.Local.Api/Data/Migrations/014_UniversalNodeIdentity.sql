PRAGMA foreign_keys=OFF;
ALTER TABLE node_identity RENAME TO node_identity_legacy;
CREATE TABLE node_identity (
    id TEXT PRIMARY KEY NOT NULL,
    node_id TEXT NULL,
    cue TEXT NULL,
    fingerprint_hash TEXT NOT NULL,
    fingerprint_components_json TEXT NOT NULL,
    enrolled_at TEXT NULL,
    last_sync_at TEXT NULL,
    credential_state TEXT NOT NULL,
    revocation_detected_at TEXT NULL,
    revocation_stage TEXT NULL
);
INSERT INTO node_identity SELECT id, node_id, cue, fingerprint_hash, fingerprint_components_json,
    enrolled_at, last_sync_at, credential_state, revocation_detected_at, revocation_stage
FROM node_identity_legacy;
DROP TABLE node_identity_legacy;
PRAGMA foreign_keys=ON;
