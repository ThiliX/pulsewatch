# PulseWatch threat model

PulseWatch makes outbound HTTP requests to URLs that its users choose. That makes **server-side request
forgery (SSRF)** the main risk. This document lists what PulseWatch protects, who might attack it, the
controls in place (with the code or config that implements each), and the risks that remain.

It was reviewed against the [OWASP SSRF Prevention Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html)
and the [OWASP ASVS](https://owasp.org/www-project-application-security-verification-standard/) V5/V12/V13
sections during Milestone 8.

## 1. What we protect

| Asset | Why it matters |
|---|---|
| The network around the server | Cloud metadata (`169.254.169.254`), databases, admin panels and other services that trust "internal" traffic. Reaching these is the classic SSRF payoff. |
| User accounts and their monitors | Monitor URLs can contain API keys in the path or query string. |
| The PostgreSQL database | Accounts (password hashes), monitors, check history, incidents, email outbox. |
| Data Protection keys | Anyone holding them can forge sign-in cookies. |
| SMTP credentials | Could be used to send spam. |
| Reputation of the server's IP | It could be used to scan or hammer third-party sites. |

## 2. Who might attack

| Actor | Can do | Goal |
|---|---|---|
| Anonymous internet user | Reach the login/register pages and the public API | Get an account, guess passwords, use PulseWatch as a free scanner. |
| Signed-in user (malicious or compromised) | Create monitors with any URL, trigger checks, read their own results | Reach internal services (SSRF), read other users' data, abuse the server as a proxy or flooder. |
| Owner of a monitored site | Control the responses, redirects and DNS answers PulseWatch receives | DNS rebinding, redirects to internal addresses, huge or endless responses, slow-loris style stalls. |
| Someone on the server's network path | See or alter traffic | Read cookies or credentials if HTTPS is missing. |

Out of scope: an attacker with a shell on the host or Docker access, a malicious administrator, and supply-chain
compromise of base images or packages (mitigated only by Dependabot and pinned versions).

## 3. Trust boundaries

```
 Browser ──HTTPS──▶ Caddy ──http (edge network)──▶ API ──▶ PostgreSQL (backend network, no internet)
                                                     ▲
                                Worker ──────────────┘
                                  │
                                  └──▶ public internet (egress network, firewalled)  ◀── untrusted targets
```

* Everything a user types (monitor names, URLs, settings) is untrusted.
* Everything a monitored target sends back (status, headers, body, redirects, DNS answers) is untrusted.
* The worker is the only component that connects to user-chosen addresses.

## 4. SSRF controls (defence in depth)

| # | Layer | What it does | Where |
|---|---|---|---|
| 1 | URL rules | Absolute `http`/`https` only; no user-info; length ≤ 2048; port allow-list (default 80, 443, 8080, 8443); host names like `localhost`, `*.local`, `*.internal`, `*.home.arpa`, `metadata`, single-label names are refused; IPv4 only in plain dotted form (`2130706433`, `0x7f.1`, `127.1` are refused). The same rules run on every redirect. | `Application/Security/DestinationValidator.cs`, `Domain/Common/AbsoluteUrl.cs` |
| 2 | Address policy | Every resolved address must be public. IPv4: private, loopback, link-local (metadata), CGNAT, documentation, benchmarking, multicast and reserved ranges are blocked. IPv6: **only global unicast `2000::/3` can pass**, then NAT64, Teredo and the rest of `2001::/23`, 6to4, documentation (`2001:db8::/32`, `3fff::/20`) are removed. IPv4-mapped addresses are unwrapped first; zoned (scope-ID) addresses are refused. If **any** DNS answer is blocked, the whole host is refused. | `Application/Security/PublicAddressPolicy.cs` |
| 3 | Connect-time pinning | The probe's `ConnectCallback` resolves the name itself, re-checks every answer and opens the socket to an address it just checked, so DNS can't change between check and use (DNS rebinding). TLS still validates the certificate against the real host name. HTTP/1.1 only, because HTTP/3 (QUIC) does not use this callback. No proxy, no cookies, no automatic redirects, no automatic decompression. | `Infrastructure/Probing/SafeConnect.cs`, `HttpProbeRunner.cs` |
| 4 | Network egress | In `docker-compose.prod.yml` the worker is the only service on the `egress` network, and host firewall rules drop its traffic to private, loopback, link-local and metadata ranges and to the host itself. The database network has no internet access. | `docker-compose.prod.yml`, [deployment.md §6](deployment.md#6-worker-egress-firewall) |

Testing never weakens production: tests swap `IAddressPolicy` / `IDnsResolver` through dependency injection in
the test projects. **There is no configuration setting that allows private addresses.**

### Limits that stop abuse as a scanner or flooder

* Minimum interval 60 s, timeout ≤ 15 s, at most 3 redirects, response body read capped at 64 KB, response
  headers capped at 64 KB, 10 concurrent checks per worker.
* Quota of 20 monitors per user; rate limits on create/update (30/min), "check now" (10/min per user and 60 s
  per monitor), and register/login (10/min per client IP).
* **Registration can be closed** (`Registration__Enabled=false`, the default in `docker-compose.prod.yml`), so a
  self-hosted server is not an open URL checker for strangers.

### What a user learns from a check

Only the outcome category, HTTP status code, duration and a fixed summary text. Response bodies and headers are
never stored or shown, and exception messages never reach the UI or emails. This keeps "blind SSRF" signals to a
minimum, but see residual risk R1.

## 5. Other controls

| Area | Control | Where |
|---|---|---|
| Authentication | ASP.NET Core Identity; passwords ≥ 12 characters (NIST 800-63B); lockout after 5 failures for 15 min; unknown emails take as long as wrong passwords | `Api/Auth/AuthSetup.cs`, `AuthEndpoints.cs` |
| Session cookies | `HttpOnly`, `Secure` outside Development, `SameSite=Strict`, 7-day sliding; keys persisted in a volume so restarts don't sign everyone out | `AuthSetup.cs`, `Api/Hosting/DataProtectionSetup.cs` |
| CSRF | Anti-forgery token required in `X-XSRF-TOKEN` on every state-changing request, plus `SameSite=Strict`; no CORS policy, so other origins can't read responses | `Api/Auth/Antiforgery.cs` |
| Access control | Every query is filtered by owner; missing **or not owned** both return the same 404 | `Api/Common/Ownership.cs`, `OwnershipTests` |
| Browser hardening | CSP `default-src 'self'` with no `unsafe-inline`/`unsafe-eval`, `frame-ancestors 'none'`, `X-Content-Type-Options`, `Referrer-Policy: no-referrer`, `Permissions-Policy`, COOP/CORP, HSTS (180 days) and HTTPS redirect outside Development; no `Server` header | `Api/Hosting/SecurityHeaders.cs`, `Program.cs` |
| Reverse proxy trust | `X-Forwarded-*` headers trusted only from the configured proxy network, one hop | `Api/Hosting/ReverseProxySetup.cs` |
| Error handling | RFC 9457 Problem Details with a trace ID; no stack traces or exception messages; no developer exception page outside Development | `Api/Errors/ExceptionHandlers.cs` |
| Email | Subjects stripped of control characters (no header injection); HTML-encoded bodies; URL shown without query string | `Application/Notifications/NotificationEmailBuilder.cs` |
| Logs | Monitor URLs logged as scheme + host only; email bodies never logged; no request/response logging on the probe client | `Application/Telemetry/LogRedaction.cs` |
| Secrets | Only in environment variables / user-secrets / `.env` (git-ignored); validated at startup; never logged | `.env.example`, `docs/deployment.md` |
| Containers | Non-root, chiseled runtime images (no shell or package manager); read-only root filesystem; `no-new-privileges`; all capabilities dropped | `src/*/Dockerfile`, `docker-compose.prod.yml` |
| Dependencies | Central package versions, Dependabot for NuGet, npm, Docker and Actions; CI builds with warnings as errors | `Directory.Packages.props`, `.github/dependabot.yml` |

## 6. Residual risks

| ID | Risk | Why it remains | What reduces it |
|---|---|---|---|
| R1 | **Blind SSRF / port probing of public hosts.** Status codes, timings and outcome categories reveal whether a public host and port respond. | That is what a monitor does. | Port allow-list, rate limits, quotas, closed registration. |
| R2 | **Public IPs that trust the server.** A public service that allow-lists the PulseWatch server's IP (for example a firewall rule "allow our office/VPS") can be reached by any user. | Address rules can't know which public IPs trust you. | Don't put the PulseWatch egress IP on any allow-list; run the worker from a dedicated IP. |
| R3 | **Internal services published on public addresses or via a public reverse proxy** (e.g. a cloud load balancer that forwards to an admin panel). | Same as R2: the address is public. | Egress firewall (layer 4) still blocks the host's own ranges; keep admin panels off the internet. |
| R4 | **DNS queries leak to the attacker's name server** when a monitor is validated or checked. | Resolving the name is required. | Harmless beyond confirming PulseWatch looked the name up. |
| R5 | **Using the server to send traffic to a third party** (at most one request per minute per monitor). | Inherent to monitoring. | 60 s minimum interval, quotas, rate limits, closed registration, per-check timeout and body cap. |
| R6 | **No MFA and no email verification.** Registration also reveals whether an email is taken. | Out of MVP scope. | Strong password rule, lockout, login rate limit, closed registration. |
| R7 | **Alert emails may be sent twice** if the worker stops after SMTP accepted a message but before it was marked sent. | At-least-once delivery. | Documented; the outbox never loses a message. |
| R8 | **Data Protection keys are stored unencrypted in a volume.** | Encrypting them needs a certificate or a key vault, which a single VM rarely has. | Volume readable only by the API container user; host access is out of scope. |
| R9 | **One worker, one location.** A network problem near the server looks like an outage. | MVP. | Emails and the UI say "checked from a single location". |

## 7. Public demo

Never run a public demo as an open URL checker. A demo must have registration closed, seeded monitors that point
at safe targets you own, and strict quotas (see [deployment.md](deployment.md)).

## 8. Reporting a vulnerability

See [SECURITY.md](../SECURITY.md).
