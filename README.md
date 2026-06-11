# Plan Cope — Monorepo

Plataforma de evaluaciones **offline-first** para la Provincia de Corrientes
(500-2000 escuelas, 20K-100K estudiantes).

---

## Roadmap — Vista Ejecutiva

| Fase | Meta | Estado | Entregables |
|------|------|--------|-------------|
| **0** | Consolidación inicial | ✅ | ADRs, stack definition, DDL_V3, contratos de sync, alcance V1 |
| **1** | Fundaciones técnicas | ✅ | Monorepo, Zod schemas, Drizzle ORM, migraciones, Docker, CI, seeders — **este commit** |
| 2 | Nodo local operativo | 🔲 | Electron + Fastify, renderer dinámico (5 tipos de bloque), delivery sessions, UI operador/estudiante |
| 3 | Builder visual | 🔲 | Editor WYSIWYG, preview runtime, versionado de contenido, workflow editorial |
| 4 | Sincronización | 🔲 | Outbox pattern, cursor pull, push idempotente, backoff exponencial, reconciliación |
| 5 | Plataforma central completa | 🔲 | Auth RBAC, gestión de nodos, publicación de paquetes, auditoría, dashboard |
| 6 | Hardening | 🔲 | Tolerancia a fallos, migración de datos legacy, empaquetado Windows (.exe), piloto |

---

## Stack

| Capa            | Tecnología                                                |
| --------------- | --------------------------------------------------------- |
| Lenguaje        | TypeScript 5.4+ (strict)                                  |
| Package manager | pnpm 11+ (workspaces)                                     |
| Build orchestr. | Turborepo 2                                               |
| Schemas         | Zod 3                                                     |
| ORM             | Drizzle 0.36 (postgres-js + better-sqlite3)               |
| HTTP            | Fastify 5 (central + local)                               |
| DB central      | PostgreSQL 16 (alpine) — vía Docker Compose              |
| DB local        | SQLite 3 (WAL, FK, busy_timeout) — embebido              |
| Lint / format   | ESLint 9 (flat config) + Prettier 3                       |
| Tests           | Vitest 2                                                  |
| CI              | GitHub Actions (PostgreSQL 16 service container)          |

---

## Requisitos

- **Node.js** ≥ 24
- **pnpm** ≥ 11 (`corepack enable && corepack prepare pnpm@11 --activate`)
- **Docker** + Docker Compose v2 (para PostgreSQL)
- **PostgreSQL 16 client** opcional (para inspección manual)

---

## Quick start (≤ 5 minutos)

```bash
# 1. Clonar e instalar dependencias
git clone <repo-url> plan-cope
cd plan-cope
pnpm install

# 2. Levantar PostgreSQL (incluye healthcheck + extensión pgcrypto)
docker compose up -d

# 3. Esperar a que el contenedor esté healthy (~10 s)
docker compose ps

# 4. Copiar las variables de entorno de las apps
cp apps/central/.env.example apps/central/.env
cp apps/local/.env.example apps/local/.env

# 5. Aplicar migraciones + seeders a la base central
pnpm db:migrate          # migrations only
pnpm db:seed             # run all seeders (geography, users, exams)

# 6. Levantar la app central
pnpm --filter @plan-cope/central dev
#   → GET http://localhost:3000/api/health → 200 { status: "ok", db: "connected", ... }

# 7. En otra terminal, levantar la app local
pnpm --filter @plan-cope/local dev
#   → GET http://localhost:3001/api/health → 200 { status: "ok", db: "connected", ... }
```

¡Listo! Las dos apps están corriendo y la base central tiene datos placeholder
de Corrientes (1 provincia → 3 departamentos → 6 localidades → 6 escuelas →
12 aulas) + 3 roles, 3 usuarios y 3 exámenes completos.

---

## Estructura del monorepo

```
.
├── apps/
│   ├── central/                  # Fastify + PostgreSQL (server de administración)
│   │   ├── src/server/app.ts     # entrypoint HTTP
│   │   ├── src/seed.ts           # entrypoint de seeders
│   │   └── drizzle.config.ts
│   └── local/                    # Fastify + SQLite (nodo offline)
│       ├── src/server/app.ts
│       ├── src/seed.ts
│       ├── electron/main.ts      # stub Electron (Fase 2)
│       └── drizzle.config.ts
│
├── packages/
│   ├── shared-schemas/           # Zod: contratos puros, sin drivers de DB
│   │   └── src/{core,exam,sync,audit,settings}.ts
│   └── shared-domain/            # Drizzle: schema + migraciones + seeders
│       ├── src/db/
│       │   ├── central/{schema,relations}.ts
│       │   ├── local/{schema,relations}.ts
│       │   ├── migrator.ts       # runMigrations(dialect-agnostic)
│       │   └── migrations/{central,local}/*.sql
│       └── src/services/
│           ├── exam.service.ts   # computeChecksum()
│           └── seeders/          # geography / users / exams / local
│
├── tooling/
│   ├── tsconfig/                 # base / node / react (compartidos)
│   ├── eslint-config/            # flat config
│   └── prettier-config/
│
├── scripts/
│   ├── validate-schemas.ts       # Zod ↔ Drizzle drift detection
│   └── reset-db.sh               # wipe + reseed el entorno local
│
├── .github/workflows/ci.yml      # GitHub Actions: lint + typecheck + build + tests
├── docker-compose.yml            # PostgreSQL 16 (alpine) + pgcrypto
├── docker/init-pgcrypto.sql      # extensión requerida por el DDL
├── DDL_V3.sql                    # fuente de verdad del modelo de datos
├── pnpm-workspace.yaml
├── turbo.json
└── package.json
```

---

## Scripts útiles

| Comando                       | Descripción                                                  |
| ----------------------------- | ------------------------------------------------------------ |
| `pnpm dev`                    | Levanta las dos apps en paralelo (watch mode)                |
| `pnpm build`                  | Compila todos los packages (tsc)                             |
| `pnpm lint`                   | ESLint sobre todo el monorepo                                |
| `pnpm typecheck`              | `tsc --noEmit` por package                                   |
| `pnpm test`                   | Vitest en todos los packages                                 |
| `pnpm db:migrate`             | Aplica migraciones pendientes (PG + SQLite)                  |
| `pnpm db:seed`                | Ejecuta los seeders (idempotentes)                            |
| `pnpm db:generate`            | Genera SQL de migración a partir del schema Drizzle         |
| `pnpm db:reset`               | Wipe + reseed (`PG_RESET_CONFIRM=yes pnpm db:reset`)         |
| `pnpm validate:schemas`       | Detecta drift entre Zod y Drizzle (falla si hay mismatch)    |
| `pnpm clean`                  | Borra `node_modules` y `dist`                                |

---

## Datos sembrados (desarrollo)

Tras `pnpm db:seed`, el sistema tiene:

### Central (PostgreSQL)

- **Geografía**: 1 provincia (Corrientes, placeholder) + 3 departamentos + 6
  localidades + 6 escuelas + 12 aulas.
- **Roles**: `admin`, `operator`, `viewer` (con descriptions).
- **Usuarios** (bcrypt cost 10, placeholders — rotar antes de prod):
  - `admin@plancope.local` / `admin123`
  - `operator@plancope.local` / `operator123`
  - `viewer@plancope.local` / `viewer123`
- **Exámenes**: 3 exámenes publicados (`EX-MAT-6-2026`, `EX-LEN-6-2026`,
  `EX-CNN-6-2026`), cada uno con 1 versión, 4–5 bloques cubriendo los 5 tipos
  V1 (`text`, `image`, `multiple_choice`, `true_false`, `short_answer`),
  opciones para los `multiple_choice` y `answer_keys` para los bloques
  calificables. Cada `exam_version` lleva un SHA-256 en
  `metadata_json->>'checksum'` (ver `computeChecksum()`).

### Local (SQLite)

- **Usuarios locales** (bcrypt cost 10):
  - `admin` / `admin123` (role: admin)
  - `operator` / `operator123` (role: operator)
- **App settings** (4 entradas):
  - `central_url` → `http://localhost:3000`
  - `jwt_secret` → 32 bytes hex (regenerado en cada seed limpio)
  - `sync_interval` → `60` (segundos)
  - `node_label` → `Nodo Local (placeholder)`

---

## Verificación de salud

```bash
# Central
curl -s http://localhost:3000/api/health | jq
# { "status": "ok", "db": "connected", "uptime": 0.43, "version": "0.1.0-fase1" }

# Local
curl -s http://localhost:3001/api/health | jq
# { "status": "ok", "db": "connected", "uptime": 0.21, "version": "0.1.0-fase1" }
```

---

## CI

El workflow `.github/workflows/ci.yml` corre en cada push y PR a `main`:

1. `pnpm install --frozen-lockfile` (falla si el lockfile está desincronizado)
2. `pnpm lint`
3. `pnpm typecheck`
4. `pnpm build`
5. `pnpm validate:schemas` (drift Zod ↔ Drizzle)
6. `pnpm test` (incluye migraciones y seeders contra PG de servicio)
7. Smoke: arranca `apps/central` y hace `GET /api/health` → 200

Cualquier fallo bloquea el merge. El job usa `concurrency: cancel-in-progress`
para no acumular runs en pushes seguidos.

---

## Troubleshooting

### "port 5432 already allocated" al hacer `docker compose up`

Ya hay un Postgres corriendo en el host (probablemente el contenedor
`plan-cope-pg-test` que arrancamos para los tests). Frenalo con:

```bash
docker stop plan-cope-pg-test
docker compose up -d
```

### "relation 'drizzle.__drizzle_migrations' does not exist"

El test de migrations de central y shared-domain corren en paralelo sobre
la misma DB. Si ves este error intermitente, es porque las pruebas del
segundo paquete tiraron el schema mientras el primero lo estaba usando.
Workaround: `pnpm db:reset` para limpiar.

### "Cannot find module '../services/exam.service.js'"

El proyecto usa ESM + bundler resolution. Asegurate de que los imports
incluyen la extensión `.js` (incluso para archivos `.ts`):

```ts
// ✅ bien
import { computeChecksum } from '../services/exam.service.js';
// ❌ mal
import { computeChecksum } from '../services/exam.service';
```

### Cambié `schema.ts` y `drizzle-kit generate` no produce migración nueva

Eso significa que el cambio es compatible con la migración existente
(típicamente cuando solo cambia el nombre de una columna en TS pero el
SQL es el mismo). Si querés forzar la regeneración, borra el contenido
de `src/db/migrations/{central,local}/` y volvé a correr
`pnpm db:generate`.

---

## Licencia

Privado / propietario — uso interno.
