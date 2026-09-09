FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props NuGet.config dotnet-tools.json ./
COPY src/Central/PlanCope.Central.Api/PlanCope.Central.Api.csproj src/Central/PlanCope.Central.Api/
COPY src/Central/PlanCope.Central.Migrations/PlanCope.Central.Migrations.csproj src/Central/PlanCope.Central.Migrations/
COPY src/Shared/PlanCope.Shared.Contracts/PlanCope.Shared.Contracts.csproj src/Shared/PlanCope.Shared.Contracts/
COPY src/Shared/PlanCope.Shared.Domain/PlanCope.Shared.Domain.csproj src/Shared/PlanCope.Shared.Domain/
COPY src/Shared/PlanCope.Shared.Infrastructure/PlanCope.Shared.Infrastructure.csproj src/Shared/PlanCope.Shared.Infrastructure/
RUN dotnet restore src/Central/PlanCope.Central.Api/PlanCope.Central.Api.csproj \
    && dotnet restore src/Central/PlanCope.Central.Migrations/PlanCope.Central.Migrations.csproj \
    && dotnet tool restore

COPY src/Central/ src/Central/
COPY src/Shared/ src/Shared/
ARG VERSION=0.0.0-local
RUN dotnet publish src/Central/PlanCope.Central.Api/PlanCope.Central.Api.csproj \
      --configuration Release --no-restore --output /out/api \
      -p:Version="${VERSION}" -p:TreatWarningsAsErrors=false \
    && dotnet build src/Central/PlanCope.Central.Migrations/PlanCope.Central.Migrations.csproj \
      --configuration Release --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
ARG SOURCE="https://github.com/geronimoserial/plan-cope"
ARG REVISION="unknown"
ARG VERSION="0.0.0-local"
LABEL org.opencontainers.image.source="${SOURCE}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.version="${VERSION}"
WORKDIR /app
RUN apt-get update \
    && apt-get install --no-install-recommends --yes curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build --chown=app:app /out/api/ ./
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=6 \
  CMD curl --fail --silent http://localhost:8080/health/live >/dev/null || exit 1
ENTRYPOINT ["dotnet", "PlanCope.Central.Api.dll"]

# Dedicated one-shot stage that applies EF Core migrations against Postgres.
# Uses `dotnet ef database update` (not `migrations bundle`): the bundle command's
# internal self-contained RID publish conflicts with the API project's own build
# output (NETSDK1152, duplicate apphost/deps.json) because the API project is an
# ASP.NET Web SDK project. Running migrations via the SDK directly avoids that
# conflict entirely and needs no extra tooling beyond what `build` already has.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS migrate
ARG SOURCE="https://github.com/geronimoserial/plan-cope"
ARG REVISION="unknown"
ARG VERSION="0.0.0-local"
LABEL org.opencontainers.image.source="${SOURCE}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.version="${VERSION}"
WORKDIR /src
COPY --from=build /src/ ./
RUN dotnet restore src/Central/PlanCope.Central.Migrations/PlanCope.Central.Migrations.csproj \
    && dotnet tool restore
ENV PATH="${PATH}:/root/.dotnet/tools"
ENTRYPOINT ["sh", "-c", "dotnet ef database update --project src/Central/PlanCope.Central.Migrations/PlanCope.Central.Migrations.csproj --startup-project src/Central/PlanCope.Central.Migrations/PlanCope.Central.Migrations.csproj --configuration Release --connection \"$ConnectionStrings__CentralDatabase\""]
