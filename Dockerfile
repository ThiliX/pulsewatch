# syntax=docker/dockerfile:1
#
# Production images for PulseWatch. One file, three targets:
#   docker build --target api        -t pulsewatch-api .        # API + built web app
#   docker build --target worker     -t pulsewatch-worker .     # scheduler, probes, email outbox
#   docker build --target migrations -t pulsewatch-migrations . # EF Core migration bundle (run once per release)
# docker-compose.prod.yml builds all three. Runtime images are chiseled Ubuntu: no shell, no package manager,
# and they run as the non-root "app" user (UID 1654).

ARG DOTNET_VERSION=10.0
ARG NODE_VERSION=24

# ---------- web app ----------
FROM node:${NODE_VERSION}-bookworm-slim AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# ---------- .NET build ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
ENV DOTNET_NOLOGO=true DOTNET_CLI_TELEMETRY_OPTOUT=true

# Restore first, from project files only, so this layer is reused until a dependency changes.
COPY global.json Directory.Build.props Directory.Packages.props dotnet-tools.json .editorconfig ./
COPY src/PulseWatch.Domain/PulseWatch.Domain.csproj src/PulseWatch.Domain/
COPY src/PulseWatch.Application/PulseWatch.Application.csproj src/PulseWatch.Application/
COPY src/PulseWatch.Infrastructure/PulseWatch.Infrastructure.csproj src/PulseWatch.Infrastructure/
COPY src/PulseWatch.Api/PulseWatch.Api.csproj src/PulseWatch.Api/
COPY src/PulseWatch.Worker/PulseWatch.Worker.csproj src/PulseWatch.Worker/
RUN dotnet restore src/PulseWatch.Api/PulseWatch.Api.csproj \
 && dotnet restore src/PulseWatch.Worker/PulseWatch.Worker.csproj \
 && dotnet tool restore

COPY src/ src/
RUN dotnet publish src/PulseWatch.Api/PulseWatch.Api.csproj -c Release --no-restore -o /out/api -p:UseAppHost=false \
 && dotnet publish src/PulseWatch.Worker/PulseWatch.Worker.csproj -c Release --no-restore -o /out/worker -p:UseAppHost=false \
 && mkdir -p /out/keys

# The bundle reads ConnectionStrings__PulseWatch from the environment (DesignTimeDbContextFactory).
RUN dotnet ef migrations bundle \
      --project src/PulseWatch.Infrastructure --startup-project src/PulseWatch.Api \
      --configuration Release --output /out/migrations/efbundle

# ---------- API (serves the web app too) ----------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled AS api
WORKDIR /app
COPY --from=build /out/api ./
COPY --from=web /web/dist ./wwwroot
# Data Protection key ring; docker-compose.prod.yml mounts a volume here.
COPY --from=build --chown=$APP_UID:$APP_UID /out/keys /keys
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "PulseWatch.Api.dll"]

# ---------- Worker ----------
FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION}-noble-chiseled AS worker
WORKDIR /app
COPY --from=build /out/worker ./
USER $APP_UID
ENTRYPOINT ["dotnet", "PulseWatch.Worker.dll"]

# ---------- Migrations ----------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled AS migrations
WORKDIR /app
COPY --from=build /out/migrations ./
USER $APP_UID
# The bundle is a single file that unpacks itself; /tmp is the only writable place (read-only root filesystem).
ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp
ENTRYPOINT ["/app/efbundle"]
