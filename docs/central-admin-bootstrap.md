# Central API — first production administrator bootstrap

## Why this exists

`PlanCope.Central.Api` deliberately has no public signup endpoint: `AuthController` only exposes
`login`, `refresh`, and `me`. The only code that used to create a user was `DevelopmentSeeder`,
which runs exclusively when `ASPNETCORE_ENVIRONMENT=Development`. In production, with an empty
`Users` table, that is a closed loop — nobody can ever log in to create anything. The
`AdminBootstrapper` (invoked unconditionally at startup in `Program.cs`, outside the development
guard) breaks that loop by creating the first administrator account from environment
configuration, exactly once, and idempotently on every restart after that.

## Required environment variables

| Variable | Purpose |
|----------|---------|
| `PLANCOPE_BOOTSTRAP_ADMIN_EMAIL` | Login email of the first administrator. |
| `PLANCOPE_BOOTSTRAP_ADMIN_PASSWORD` | Initial password. **At least 12 characters.** The literals `Admin123!` and `password` are rejected case-insensitively (guards against copy-pasting the development seed default). |
| `PLANCOPE_BOOTSTRAP_ADMIN_FULL_NAME` | Display name shown in the UI. |

All three are read through the standard ASP.NET Core configuration pipeline, so plain
environment variables work as shown (they can also be provided via any other configuration
provider).

If any variable is missing or blank, or the password is shorter than 12 characters or is one of
the rejected defaults, the bootstrapper throws `InvalidOperationException` naming the offending
variable. The exception is **not** swallowed: startup crashes. A deployment with bad bootstrap
config fails fast and visibly instead of coming up with no way to log in. Consequently, the three
variables must be set in **every** environment the API runs in, including local Development.

## Safe to leave the variables set

The bootstrap is idempotent and never overwrites:

- If no user exists with the configured email, it is created with a BCrypt password hash, status
  `Active`, and the `Admin` role.
- If the user already exists, `PasswordHash` and every other field are left untouched. The run
  only ensures the `Admin` role assignment exists (healing a half-applied previous run).

You can keep the variables set permanently and redeploy or restart as often as you like — an
existing administrator password is never rotated or clobbered by this process. To change the
admin password later, do it through the application/database, not through these variables.

The bootstrapper creates only the user, the `Admin` role (if missing), and the role assignment.
It does not create `UserSchoolAssignment`, `Province`, `Department`, `Locality`, or `School` rows
— roster scope is real-data work handled separately.

## Logging

- `Information` when a new administrator is created (email address only).
- `Debug` when the account already exists (no-op), so routine restarts do not spam production logs.
- The password and the password hash are **never** logged, at any level, on any path.

## Deployment example

This API is deployed as a Docker image (`src/Central/PlanCope.Central.Api/Dockerfile`): GitHub
Actions publishes it to GHCR and Coolify consumes it in production; local/CI parity uses the
Compose files under `deploy/`. Configuration is supplied as container environment variables on
the `api` service. The existing `deploy/compose.ci.yml` and `deploy/compose.dev.yml` do not
define the bootstrap variables yet — add the three `PLANCOPE_BOOTSTRAP_ADMIN_*` keys to the
`environment` block of the `api` service (the bootstrapper crashes startup without them):

```yaml
services:
  api:
    image: ghcr.io/<org>/plan-cope-central-api:<version>
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__CentralDatabase: ${ConnectionStrings__CentralDatabase:?required}
      Auth__SigningKey: ${Auth__SigningKey:?required}
      GeApi__Username: ${GeApi__Username:?required}
      GeApi__Password: ${GeApi__Password:?required}
      PLANCOPE_BOOTSTRAP_ADMIN_EMAIL: ${PLANCOPE_BOOTSTRAP_ADMIN_EMAIL:?required}
      PLANCOPE_BOOTSTRAP_ADMIN_PASSWORD: ${PLANCOPE_BOOTSTRAP_ADMIN_PASSWORD:?required}
      PLANCOPE_BOOTSTRAP_ADMIN_FULL_NAME: ${PLANCOPE_BOOTSTRAP_ADMIN_FULL_NAME:?required}
```

In production (Coolify), set the same three keys as environment variables on the Central API
application. The equivalent raw form:

```bash
docker run -e PLANCOPE_BOOTSTRAP_ADMIN_EMAIL=admin@example.org \
  -e PLANCOPE_BOOTSTRAP_ADMIN_PASSWORD="a-strong-passphrase-of-12-plus-chars" \
  -e PLANCOPE_BOOTSTRAP_ADMIN_FULL_NAME="Jane Doe" \
  ghcr.io/<org>/plan-cope-central-api:<version>
```

For local development, add the three variables to your shell or `.env` used by
`deploy/compose.dev.yml` — they are required there too, because the bootstrapper runs in every
environment.

Note that the bootstrapper runs before the HTTP pipeline and is not wrapped in a try/catch, so
database migrations must be applied before the first start in any environment — otherwise
startup fails on the missing `core.users` table rather than with a friendly warning.
