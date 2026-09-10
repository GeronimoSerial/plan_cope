# Encrypted roster bundle format

`PlanCope.RosterCrypto` produces a versioned binary container. All integers are little-endian.
The packer derives a 256-bit KEK from the passphrase with Argon2id and a random salt per bundle.
Each entry gets an independent random 256-bit DEK; the DEK and the JSON are authenticated
separately with AES-256-GCM.

## Header

| Field | Size (bytes) | Description |
| --- | ---: | --- |
| Magic | 8 | ASCII `PCRSTR01` |
| Version | 2 | Currently `1` |
| Argon2id memory | 4 | Configurable KiB |
| Argon2id iterations | 4 | Configurable |
| Argon2id parallelism | 4 | Configurable |
| Argon2id salt | 16 | Random per bundle |
| Entry count | 4 | Number of consecutive entries |

## Entry

| Field | Size (bytes) | Description |
| --- | ---: | --- |
| CUE | 9 | ASCII digits; entry index |
| JSON nonce | 12 | Random AES-GCM nonce |
| JSON tag | 16 | AES-GCM tag |
| Wrap nonce | 12 | Random AES-GCM nonce |
| Wrap tag | 16 | AES-GCM tag for the DEK |
| Wrapped DEK | 32 | DEK encrypted with the derived KEK |
| Checksum | 32 | SHA-256 of the original JSON |
| Ciphertext length | 4 | Unsigned integer, bounded in practice by the reader |
| Ciphertext | variable | JSON encrypted with the DEK |

The AAD includes the magic, version, CUE, and checksum. This means altering metadata, ciphertext,
tags, or the wrapped DEK breaks authentication for that entry. The reader walks the metadata and
skips ciphertexts until it finds the requested CUE; it decrypts only that entry. The tool also
emits `<bundle>.manifest.json` with the version, Argon2id parameters, CUE, offset, length, and
checksum; it never includes documents or names. `RosterEntryNotFoundException` explicitly
represents a missing CUE.

The default values (19 MiB, 2 iterations, parallelism 1) are configurable via the CLI and are
self-contained in the header. They must be measured and tuned for field hardware before a
production release.

Local.Api is configured via `RosterBundle:Path`, `RosterBundle:Cue`, and
`RosterBundle:Passphrase` (environment equivalents: `RosterBundle__Path`, `RosterBundle__Cue`,
and `RosterBundle__Passphrase`). The passphrase must never be stored in versioned files.
