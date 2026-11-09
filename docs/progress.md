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

## Milestone 2 — Auth and secure monitor API (9 Nov 2026)

**Built**
- **Accounts** (`src/PulseWatch.Api/Auth`): ASP.NET Core Identity with cookie auth.
  - `pw.auth` cookie: HttpOnly, SameSite=Strict, Secure outside Development, 7-day sliding.
  - Passwords ≥ 12 characters (length over composition rules); lockout after 5 failures for 15 minutes.
  - Endpoints: `GET /auth/antiforgery`, `POST /auth/register`, `POST /auth/login`, `POST /auth/logout`, `GET /auth/me`.
  - 401/403 instead of login redirects. Wrong password, unknown email and locked account all give the same 401; unknown emails still run a password hash so timing doesn't reveal them.
- **CSRF**: double-submit anti-forgery token. The `XSRF-TOKEN` cookie value must come back in `X-XSRF-TOKEN` on every POST/PATCH/DELETE; the token is bound to the user and re-issued after sign-in and sign-out.
- **Rate limits** (fixed one-minute windows, configurable): auth 10 per IP, monitor writes 30 per user, check-now 10 per user, plus a 60 s cooldown per monitor. 429 responses carry `Retry-After`.
- **URL safety, SSRF layers 1–2** (`src/PulseWatch.Application/Security`, reusable by the probe in M3):
  - `PublicAddressPolicy`: blocks private, loopback, link-local (incl. 169.254.169.254), CGNAT, documentation, benchmarking, multicast and reserved IPv4/IPv6 ranges; unwraps IPv4-mapped IPv6; blocks NAT64, 6to4 and Teredo prefixes and scoped IPv6.
  - `DestinationValidator`: http/https only, no user-info, allowed ports (80, 443, 8080, 8443), local host names (`localhost`, `*.local`, `*.internal`, `*.home.arpa`, single-label names, metadata names), odd IPv4 spellings (`2130706433`, `0x7f.1`, `127.1`, `0177.0.0.1`), and DNS answers — rejected if **any** answer is non-public. Error messages never echo internal IPs.
- **Monitor API** (`src/PulseWatch.Api/Monitors`): list (paged, state filter), create, get, PATCH, delete, pause, resume, check-now.
  - Every query goes through one `Owned(user)` helper; another user's id gives the same 404 as a missing id.
  - Quota per user (default 20), enforced inside a transaction that locks the user row.
  - ETag / `If-Match` optimistic concurrency → 412.
  - PATCH re-checks DNS only when the URL actually changes; changing the URL resets health counters.
- Domain additions: `UpdateDetails`, `TryRequestManualCheck` (cooldown), `IsStale` (no result for 2 intervals + 60 s).
- Errors: Problem Details everywhere with a `traceId`; domain rule violations → 400, concurrency → 412, malformed JSON → 400 (found by a test: it was a 500 in Development). No stack traces in responses.
- OpenAPI document with cookie and anti-forgery security schemes; Scalar API reference at `/scalar` in Development.

**Tests (261 .NET tests in total, all passing)**
- 191 unit tests, including ~90 address cases for the IP policy and destination validator (fake DNS), editing/cooldown/staleness rules, and request validation (defaults, stricter operator limits, DNS only on URL change).
- 70 integration tests against PostgreSQL:
  - Auth: cookie flags, field errors, duplicate email, identical 401s, login/logout, lockout, 401 not redirect, missing XSRF header, XSRF token from another session.
  - Monitors: defaults, all field errors, 7 unsafe destinations, quota, paging/filtering, PATCH partial update, unsafe new URL, stale/current If-Match, pause/resume, check-now cooldown and paused, delete with history, malformed JSON.
  - Ownership: user B gets the same 404 as a missing id on all 6 per-monitor routes and Alice's data is unchanged; listing never shows other users' monitors; anonymous callers get 401 on all 8 routes.
  - Rate limits: login per client; writes per user (other users unaffected).
- Manual check with curl against Docker Compose: register → create → list works; `169.254.169.254` and `localtest.me` (a public DNS name for 127.0.0.1) rejected; missing XSRF header → 400; OpenAPI lists 10 routes; no passwords in logs.

**Known limitations (documented in README)**
- Registration reveals whether an email is taken (no email confirmation).
- Rate limits use the client IP; forwarded headers for reverse proxies come in M8.
- Data-protection keys aren't persisted yet (M8).
- SSRF layers 3 (connect-time address pinning) and 4 (egress firewall) come with the probe engine (M3) and deployment (M8). Nothing makes outbound requests yet.

**After M2:** `GET /api/v1/auth/antiforgery` also returns `{ token, headerName }` in the body (safe: no CORS), so the API can be tried from the Scalar docs page. `.claude/launch.json` starts the API (5080) and frontend (5173) as preview servers.

## Milestone 3 — Probe engine and scheduler (9 Nov 2026)

**Built**
- **SSRF layer 3** (`src/PulseWatch.Infrastructure/Probing/SafeConnect.cs`): the probe HttpClient's `ConnectCallback` resolves the host itself, rejects the request if any answer is blocked, and connects to an address it just checked. TLS still validates against the real host name. No proxy, no cookies, no automatic redirects, no decompression.
- `IAddressPolicy` (production: `PublicOnlyAddressPolicy`). Tests that need a local target swap it through DI; there is no configuration switch to relax it.
- **Probe runner** (`HttpProbeRunner`): one GET per check, never retried; one overall timeout; manual redirects (max 3) with full re-validation of every target, loop detection; at most 64 KB of body read; an expected 3xx counts as success without following. Outcomes are classified (Success, HttpStatusFailure, Timeout, DnsFailure, TlsFailure, ConnectionFailure, RedirectRejected, DestinationBlocked, UnexpectedError) with fixed safe summaries. Default HttpClient logging is removed (it would log full URLs).
- **Claiming** (`MonitorClaimer`): one `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING` statement leases due monitors; leases expire after probe timeout + 30 s.
- **Recording** (`CheckResultRecorder`): one transaction locks the monitor row, inserts the check, runs the incident state machine (opens incidents at the threshold), schedules the next check (one interval after the check started, or now if a manual check was requested meanwhile) and releases the lease. Results are dropped if the monitor was deleted or its URL changed during the check; a pause during the check keeps the result but changes no incident.
- **Scheduler** (`SchedulerService`): polls every 5 s, drains backlogs batch by batch, runs up to 10 probes at once, isolates failures per monitor, keeps going when the database is down, and on shutdown stops claiming while in-flight checks finish (30 s shutdown timeout).
- **Heartbeat**: upserts `worker_heartbeats` (worker id defaults to the machine name); failures are logged, never fatal.
- `AddPulseWatchWorker` wires the worker host; EF's per-statement SQL logs turned down to Warning.

**Tests (310 .NET tests in total, all passing)**
- 229 unit tests, new: probe outcomes (status, expected 3xx, relative redirects, 6 blocked redirect targets, loops, redirect limit, header timeout, stalled body, endless body capped, caller cancellation, 8 transport failure categories, no exception text stored), real-socket connect tests (connects to the vetted address; production policy refuses loopback, mixed and IPv4-mapped answers before connecting; IP literals; DNS failures; real HTTP end to end; **DNS rebinding blocked at connect time**; closed port), and next-check scheduling rules.
- 81 integration tests, new: claim filtering/order/batch size, lease expiry and reclaim, **8 concurrent claimers over 60 monitors with no duplicates**, recording success/failures/incident, deleted/URL-changed/paused during a check, heartbeat upsert, and the real worker host (due monitors checked exactly once, paused and future skipped, a crashing probe isolated and not retried in a hot loop, incident opened, heartbeat written, survives an unreachable database).
- Manual run against Docker Compose: the worker checked the GitHub status URL (Success 200 in 596 ms), the monitor became Healthy, the next check was due ~60 s later, the lease was released and the heartbeat row written; no URLs or passwords in the log.

**Known limitations**
- An edit that lands at the exact moment the worker claims or records the same monitor gets 412 and must be retried (both touch the row's version).
- Scheduling tolerance is about one poll interval plus queueing; worker lag metrics come in M7.
- SSRF layer 4 (egress firewall) is a deployment task (M8).

## Milestone 4 — Incidents end to end (9 Nov 2026)

**Built**
- **Check history**: `GET /api/v1/monitors/{id}/checks?fromUtc=&toUtc=&page=&pageSize=`, newest first, default window the last 24 hours, window limited to the retention period (30 days), bounded pages.
- **Incidents**: `GET /api/v1/monitors/{id}/incidents`, `GET /api/v1/incidents?status=open|resolved`, `GET /api/v1/incidents/{id}` (with notification history from the outbox, ready for M5). Responses include the monitor name, duration from the first failure (to now while open), failure count and the opening evidence (outcome and status code).
- Shared `Ownership` helpers (`OwnedMonitors`, `OwnedIncidents`) so every list and detail query starts from the signed-in user's monitors; shared `Paging` limits.
- **Duplicate-incident safety, by design**: the recorder locks the monitor row (`FOR UPDATE`) before reading the open incident, and inserting an incident needs a key-share lock on that same row (foreign key), so two writers are serialised by PostgreSQL itself. The partial unique index stays as the last line of defence. A separate unique-violation retry was therefore not added: it would be unreachable code.

**Tests (326 .NET tests in total: 229 unit, 97 integration; all passing)**
- 97 integration tests, new:
  - **Lifecycle (exit gate)**: monitor created through the real API; a real worker with the real probe engine checks a local HTTP server switched 200 → 500 → 200. Verified step by step: healthy; two failures = Degraded, no incident; third failure opens exactly one incident dated from the first failure; more failures add to it; **worker restarted mid-incident** with no duplicate; first success (recovery threshold 2) keeps it open; second success resolves it exactly once; later successes change nothing; the API shows 9 checks and 1 resolved incident with 5 failures. Repeated 3 extra times: stable.
  - Two recorders writing failures for the same monitor at the same moment → one incident with 2 failures.
  - History endpoints: default 24 h window, explicit window, newest-first paging, invalid windows/paging rejected, per-monitor and global incident lists, status filter, details with notifications, durations.
  - Ownership: the 2 new per-monitor routes return the same 404 for another user's monitor; another user's incident id looks missing and never appears in lists; anonymous callers get 401 on the incident routes.
- Test infrastructure: the test PostgreSQL allows 500 connections and each test database uses a small pool (the growing suite hit the default limit of 100).
- Manual check against Docker Compose: signed in, read the GitHub status monitor's stored check (200, 596 ms) and the (empty) incident list; OpenAPI lists 14 routes.
