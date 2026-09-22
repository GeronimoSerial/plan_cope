#!/usr/bin/env python3
"""Populate a migrated, local Central API with review-only data.

Requires the Development admin account to exist. Uses public API contracts so
publication packages and activation-key hashes are produced by the application.
"""

import argparse
import json
import os
import stat
import sys
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import Request, urlopen


EXAM_CODE = "DEMO-CENTRAL-6-2026"
KEY_NOTE = "Demo local para revisión de Central"


def request(base, path, *, token=None, method="GET", payload=None):
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    headers = {"Accept": "application/json"}
    if data is not None:
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = Request(f"{base}{path}", data=data, headers=headers, method=method)
    try:
        with urlopen(req, timeout=30) as response:
            body = response.read()
            return json.loads(body) if body else None
    except HTTPError as error:
        detail = error.read().decode("utf-8", errors="replace")[:500]
        raise RuntimeError(f"{method} {path}: HTTP {error.code}: {detail}") from error
    except URLError as error:
        raise RuntimeError(f"{method} {path}: {error.reason}") from error


def seed_exam(base, token):
    exams = request(base, "/api/exams", token=token)
    existing = next((exam for exam in exams if exam["code"] == EXAM_CODE), None)
    if existing:
        print(f"Examen ya existente: {EXAM_CODE} ({existing['id']})")
        return existing["id"]

    exam = request(base, "/api/exams", token=token, method="POST", payload={
        "code": EXAM_CODE,
        "title": "Examen de muestra para revisar Central",
        "description": "Contenido ficticio de desarrollo; no usar en una toma real.",
        "level": "Primaria",
        "area": "Demostración",
        "subject": "Matemática",
    })
    version = request(base, f"/api/exams/{exam['id']}/versions", token=token, method="POST", payload={
        "schemaVersion": 1,
        "metadata": {"title": exam["title"], "grade": "6", "division": "Demo", "subject": "Matemática"},
        "scoringPolicy": "AllOrNothing",
    })
    request(base, f"/api/exams/versions/{version['id']}/document", token=token, method="PUT", payload={
        "metadata": {"title": exam["title"], "grade": "6", "division": "Demo", "subject": "Matemática"},
        "scoringPolicy": "AllOrNothing",
        "blocks": [
            {"orderIndex": 0, "blockType": "Text", "title": "Bienvenida", "description": None,
             "config": {"content": "Examen ficticio para revisar el sistema Central."},
             "validation": {"required": False}, "correctAnswer": None, "scoreValue": 0},
            {"orderIndex": 1, "blockType": "MultipleChoice", "title": "Suma", "description": None,
             "config": {"question": "¿Cuánto es 18 + 24?", "multiple": False,
                        "options": [{"value": "38", "label": "38"}, {"value": "42", "label": "42"},
                                    {"value": "44", "label": "44"}]},
             "validation": {"required": True}, "correctAnswer": ["42"], "scoreValue": 1},
            {"orderIndex": 2, "blockType": "TrueFalse", "title": "Múltiplos", "description": None,
             "config": {"question": "El número 9 es múltiplo de 3."},
             "validation": {"required": True}, "correctAnswer": True, "scoreValue": 1},
            {"orderIndex": 3, "blockType": "ShortAnswer", "title": "Explicación", "description": None,
             "config": {"prompt": "Explicá cómo resolverías 15 × 4."},
             "validation": {"required": True}, "correctAnswer": None, "scoreValue": 0},
        ],
    })
    request(base, f"/api/exams/versions/{version['id']}/publish", token=token, method="POST", payload={
        "subject": "Matemática", "grade": "6", "division": "Demo",
    })
    print(f"Examen publicado: {EXAM_CODE} ({exam['id']})")
    return exam["id"]


def seed_key(base, token, key_file):
    keys = request(base, "/api/admin/activation/keys", token=token)
    if any((key.get("note") or "").startswith(KEY_NOTE) for key in keys):
        print("Clave demo ya existente; no se emite otra.")
        return
    if key_file.exists():
        raise RuntimeError(f"Existe {key_file}, pero la clave no aparece en Central. Revisar antes de reemitir.")
    key = request(base, "/api/admin/activation/keys", token=token, method="POST", payload={
        "maxActivations": 2, "expiresAt": None, "note": KEY_NOTE,
    })
    fd = os.open(key_file, os.O_WRONLY | os.O_CREAT | os.O_EXCL, stat.S_IRUSR | stat.S_IWUSR)
    with os.fdopen(fd, "w", encoding="utf-8") as output:
        output.write(key["plaintextKey"] + "\n")
    print(f"Clave demo emitida; valor guardado con permisos 0600 en {key_file}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api-url", default="http://127.0.0.1:8081")
    parser.add_argument("--key-file", type=Path, default=Path("/tmp/plancope-central-demo-key"))
    args = parser.parse_args()
    parsed = urlparse(args.api_url)
    if parsed.scheme != "http" or parsed.hostname not in {"localhost", "127.0.0.1", "::1"}:
        parser.error("El seed de desarrollo sólo acepta una API HTTP en localhost.")
    password = os.environ.get("PLANCOPE_DEV_ADMIN_PASSWORD")
    if not password:
        parser.error("Definí PLANCOPE_DEV_ADMIN_PASSWORD para el usuario admin@plancope.test.")

    base = args.api_url.rstrip("/")
    login = request(base, "/api/auth/login", method="POST", payload={
        "username": "admin@plancope.test", "password": password,
    })
    token = login["accessToken"]
    seed_exam(base, token)
    seed_key(base, token, args.key_file)


if __name__ == "__main__":
    try:
        main()
    except RuntimeError as error:
        print(error, file=sys.stderr)
        raise SystemExit(1) from error
