# Progress log

One entry per milestone: what was built, how it was verified, and what is left.

## Milestone 0 — Repository and scaffold (8 Nov 2026)

**Built**
- Git repository, `.gitignore`, `.gitattributes` (LF line endings), `.editorconfig` with naming and style rules.
- `global.json` (SDK 10.0.400 band, Microsoft.Testing.Platform runner), `Directory.Build.props` (net10.0, nullable, warnings as errors, code style enforced on build), `Directory.Packages.props` (central package versions).
- `PulseWatch.slnx` with Domain, Application, Infrastructure, Api, Worker, UnitTests and IntegrationTests, referenced in the planned direction.
- API: `/health/live` (no checks) and `/health/ready` (only checks tagged `ready`; none registered until M1), Problem Details, OpenAPI in Development, HSTS + HTTPS redirection outside Development. Fixed dev port 5080.
- Worker: `HeartbeatService` with validated options (`Worker:HeartbeatSeconds`, 5–300) and clean shutdown.
- Docker Compose: PostgreSQL 18.6 with health check and named volume, Mailpit for local email; both bound to 127.0.0.1; password required from `.env`.
- `.env.example` with placeholders only.
- React + TypeScript (strict) + Vite + Tailwind + React Router shell. The dashboard shows whether the API is reachable via `/health/ready`, with loading, not-ready and unreachable states and a retry button.
- GitHub Actions CI (backend: restore, format check, build, test; frontend: format check, lint, typecheck, test, build) and Dependabot.
- README, MIT license, this log.

**Tests**
- Unit: Domain references only the base class library; Application doesn't reference EF Core, ASP.NET Core, Npgsql, Infrastructure, Api or Worker.
- Integration: `/health/live` and `/health/ready` return 200 `Healthy`; unknown route returns 404; worker host starts/stops cleanly; invalid heartbeat setting fails at startup.
- Frontend: API status shows reachable / not ready (with status code) / unreachable, and retry recovers; routes render dashboard and not-found pages.

**Left for later milestones**
- Readiness check against the database (M1).
- `CONTRIBUTING.md`, `SECURITY.md`, code of conduct and issue templates (M8).
- CI hasn't run on GitHub yet — the repository has no remote.

## Milestone 1 — Domain and persistence (9 Nov 2026)

**Built**
- Domain (`src/PulseWatch.Domain`), with no framework dependencies:
  - `EndpointMonitor` (named so to avoid clashing with `System.Threading.Monitor`), `MonitorCheck`, `Incident`, `NotificationOutboxMessage`, `WorkerHeartbeat`.
  - `MonitorLimits` (hard limits), `MonitorSettings`, `MonitorUrl` (syntax only: http/https, no user-info, no fragment, ≤ 2048 chars — the SSRF check comes in M2/M3).
  - The incident state machine in `EndpointMonitor.ApplyCheckResult`: failure threshold, recovery threshold, streak reset, paused monitors, one incident per streak, first-failure time kept.
  - Outbox idempotency key `incident:event:channel:RECIPIENT`; recipients with line breaks rejected (header injection).
- Persistence (`src/PulseWatch.Infrastructure/Persistence`):
  - `PulseWatchDbContext` (with ASP.NET Core Identity tables), snake_case naming, Identity tables renamed to `users`, `roles`, etc.
  - CHECK constraints mirroring `MonitorLimits`; cascade deletes (user → monitors → checks/incidents → outbox).
  - Partial unique index `ux_incidents_one_open_per_monitor` (`WHERE status = 0`).
  - Indexes for due monitors (`WHERE is_enabled`), check history and incidents (newest first), and due outbox messages.
  - `xmin` as an optimistic concurrency token on monitors.
  - Initial migration `InitialSchema`; `dotnet-ef` 10.0.11 as a local tool.
- API: real readiness check (database reachable + no pending migrations, 5 s timeout); clear startup error when the connection string is missing; Development-only `--migrate` switch (refused with exit code 1 elsewhere).
- `MonitoringOptions` (section `Monitoring`), validated at startup; it can only narrow the hard limits.
- Migration files are marked as generated code in `.editorconfig` so analyzers skip them.

**Tests (95 .NET tests in total, all passing)**
- 74 unit tests: state machine boundaries (threshold 1/2/3, recovery 1/2, failure between recovery successes, no duplicate open/resolve, paused, pause/resume idempotent, foreign checks/incidents rejected, durations), monitor creation and URL rules, check and outbox rules, options validation, dependency rules.
- 21 integration tests against PostgreSQL 18.6 (Testcontainers): migration on an empty database, round trip of UTC times and enums, second open incident rejected by the unique index, resolved incidents don't block a new one, 5 CHECK constraints, duplicate idempotency key rejected, cascades, concurrency token, readiness healthy/pending/unreachable, liveness independent of the database, missing connection string message, worker host.
- Manual check with Docker Compose: `/health/ready` returned 503 before `--migrate` and 200 after; `--migrate` refused in Production; no password in API logs.

**Left for later milestones**
- Monitor state transitions for edits (URL change resets counters) arrive with the PATCH endpoint in M2.
- Outbox delivery transitions (sending, retry, exhausted) arrive in M5.
