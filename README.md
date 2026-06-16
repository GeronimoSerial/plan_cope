# Plan Cope — Informe Ejecutivo de Estado

**Plataforma de evaluaciones offline-first para la Provincia de Corrientes.**
500–2000 escuelas · 20K–100K estudiantes · .NET 8 · PostgreSQL + SQLite

---

## Estado general: Fase 1 — Fundaciones técnicas (cerrada)

**Resultado de la fase**: base técnica completa y compilando en limpio (`dotnet build PlanCope.slnx`), lista para pasar a implementación funcional en la siguiente etapa.

| Área | Estado | Detalle |
|---|---|---|
| Modelo de dominio | ✅ Completo | 22 entidades centrales + 13 locales (records inmutables) |
| Value objects | ✅ Completo | `ExamCode`, `CueCode`, `Grade` con validación |
| Enums | ✅ Completo | 8 tipos (`BlockType`, `ExamStatus`, `SyncDirection`, etc.) |
| Contratos API (DTOs) | ✅ Completo | 29 tipos de request/response + source-gen JSON |
| Validación (FluentValidation) | ✅ Completo | 5 validadores registrados en DI compartido |
| Central — EF Core | ✅ Esquema | DbContext + 24 entity configurations (6 esquemas PostgreSQL) |
| Central — Migraciones SQL | ✅ Preparado | `DbContext` y configuraciones EF listas; generación de migraciones pasa a la fase 2 |
| Central — Controladores | ✅ Bootstrap | `HealthController` operativo; endpoints funcionales quedan para la fase 2 |
| Local — SQLite + DbUp | ✅ Completo | 11 tablas, 9 índices, migraciones embebidas (001 + 002) |
| Local — Repositorios Dapper | ✅ Completo | 6 repositorios con SQL completo (User, Exam, Session, Attempt, Outbox, SyncState) |
| Local — API bootstrap | ✅ Completo | API local arranca, inicializa SQLite y expone health endpoint |
| Local — WinForms Host | ✅ Shell | `MainForm` 1280×800, embebe la API local |
| Sync — Outbox local | ✅ Esquema | Tabla `sync_outbox` con reintentos y backoff |
| Sync — Motor | ⏭️ Fase 2 | Contratos definidos; falta worker/service |
| Auth / JWT | ⏭️ Fase 2 | Paquetes declarados; falta middleware y endpoints |
| Logging (Serilog) | ⏭️ Fase 2 | Paquete declarado; falta configuración |
| Swagger | ⏭️ Fase 2 | Paquete declarado; falta cableado |
| Hashing (BCrypt) | ⏭️ Fase 2 | Paquete declarado; falta integración |
| Docker Compose | ⏭️ Fase 2 | Carpeta `deploy/` creada; faltan archivos |
| Frontend (React/Vite) | ⏭️ Fase 2 | Workspace aún no creado |
| Tests | ⏭️ Fase 2 | Proyectos creados; falta implementar casos |
| CI/CD | ⏭️ Fase 2 | Sin workflows todavía |

---

## Arquitectura

```
┌──────────────────────────────────┐     ┌──────────────────────────────┐
│         CENTRAL (Nube)           │     │       LOCAL (Escuela)        │
│                                  │     │                              │
│  PostgreSQL 16                   │     │  SQLite (WAL)                │
│  EF Core (6 schemas)             │     │  Dapper + DbUp               │
│  ASP.NET Core Web API            │     │  ASP.NET Core Web API        │
│                                  │     │  WinForms Desktop Host       │
│  ┌──────────────────────────┐   │     │                              │
│  │ Sync Protocol:           │   │     │  ┌────────────────────────┐  │
│  │  Pull (cursor-based) ◄───┼───┼─────┼──┤ sync_outbox (push)     │  │
│  │  Push (idempotent) ──────┼───┼─────┼──┤ con reintentos         │  │
│  └──────────────────────────┘   │     │  └────────────────────────┘  │
└──────────────────────────────────┘     └──────────────────────────────┘
```

**Decisión clave**: dos ORM distintos — EF Core para el modelo relacional complejo del central, Dapper + SQL crudo para el nodo local liviano.

---

## Stack tecnológico

| Capa | Tecnología | Versión |
|---|---|---|
| Runtime | .NET | 8.0 |
| Central DB | PostgreSQL + Npgsql EF Core | 8.0.4 |
| Local DB | SQLite + Dapper + DbUp | 8.0.6 / 2.1.35 / 5.0.40 |
| Validación | FluentValidation | 11.11.0 |
| Serialización | System.Text.Json (source-gen) | — |
| Desktop | Windows Forms | — |
| Auth (plan) | JWT Bearer + BCrypt | 8.0.6 / 4.0.3 |
| Logging (plan) | Serilog | 8.0.1 |
| API Docs (plan) | Swashbuckle | 6.6.2 |
| Actualización (plan) | Velopack | 0.0.1191 |
| Embed. browser (plan) | WebView2 | 1.0.2792.45 |

---

## Estructura del repositorio

```
.
├── PlanCope.slnx
├── Directory.Build.props          # Nullable, ImplicitUsings, TreatWarningsAsErrors
├── Directory.Packages.props       # Versiones centralizadas de NuGet
├── global.json                    # SDK 10.0.301
├── NuGet.config
├── src/
│   ├── Shared/
│   │   ├── PlanCope.Shared.Domain/        # Entidades, value objects, enums
│   │   ├── PlanCope.Shared.Contracts/     # DTOs, source-gen JSON context
│   │   └── PlanCope.Shared.Infrastructure/ # FluentValidation, DI extensions
│   ├── Central/
│   │   ├── PlanCope.Central.Api/          # Web API + EF Core DbContext
│   │   └── PlanCope.Central.Migrations/   # Design-time factory
│   └── Local/
│       ├── PlanCope.Local.Api/            # Web API + Dapper repos + DbUp
│       └── PlanCope.Local.Host/           # WinForms desktop shell
├── tests/
│   ├── PlanCope.Central.Api.Tests/
│   ├── PlanCope.Local.Api.Tests/
│   ├── PlanCope.Shared.Tests/
│   ├── PlanCope.E2E.Tests/
│   └── PlanCope.SyncCompat.Tests/
└── deploy/                                # Docker y assets de despliegue (vacío)
```

---

## Próxima fase: Implementación funcional

1. **Generar migraciones PostgreSQL** — `dotnet ef migrations add InitialCreate` en Central
2. **Implementar controladores REST** — Auth, Exam, Sync, Publication en Central y Local
3. **Cablear autenticación JWT** — middleware + endpoints login/refresh
4. **Implementar motor de sync** — outbox worker local + pull/push endpoints en central
5. **Docker Compose** — PostgreSQL + Central API para desarrollo local
6. **Frontend React/Vite** — crear workspace `src/Frontend`
7. **Tests** — unitarios para validadores, integración para repositorios, e2e para sync
8. **CI/CD** — GitHub Actions (build + test + lint)
9. **Hardening** — Serilog, Swagger, Velopack auto-updater, empaquetado .exe
