# Deploying PulseWatch

One supported setup: **a single Linux VM running Docker Compose**. Caddy handles HTTPS, the API serves the web app,
the worker runs checks, and PostgreSQL stores everything.

```
                Internet
                   │ 80/443
             ┌─────▼─────┐
             │   Caddy   │  HTTPS (Let's Encrypt), HTTP→HTTPS redirect
             └─────┬─────┘
          edge     │ http :8080
             ┌─────▼─────┐
             │    API    │  web app + /api/v1 + /health
             └─────┬─────┘
       backend     │  (internal network: no internet)
   ┌───────────────┼──────────────┐
┌──▼───┐     ┌─────▼──────┐
│Worker│────▶│ PostgreSQL │  volume pulsewatch-db, nightly pg_dump
└──┬───┘     └────────────┘
   │ egress (firewalled: public internet only)
   ▼
monitored sites, SMTP
```

Contents: [1 Server](#1-server) · [2 Configure](#2-configure) · [3 First start](#3-first-start) ·
[4 Create your account](#4-create-your-account) · [5 Upgrades](#5-upgrades-and-migrations) ·
[6 Worker egress firewall](#6-worker-egress-firewall) · [7 Backups](#7-backups) · [8 Logs and monitoring](#8-logs-and-monitoring) ·
[9 Configuration reference](#9-configuration-reference) · [10 Limits](#10-limits-of-this-setup)

## 1. Server

* Linux VM with 1 vCPU and 1–2 GB RAM is enough for hundreds of monitors (see [load-test.md](load-test.md)).
* Docker Engine 25+ with the Compose plugin, using Docker's default iptables firewall.
* A DNS name (A/AAAA record) pointing at the VM, and ports 80 and 443 open to the internet. Keep every other port
  closed (e.g. `ufw default deny incoming; ufw allow 22,80,443/tcp`).
* Don't run other services on the same VM that trust "local" traffic (see [threat-model.md R2/R3](threat-model.md#6-residual-risks)).

## 2. Configure

```bash
sudo mkdir -p /opt/pulsewatch && sudo chown "$USER" /opt/pulsewatch
git clone https://github.com/ThiliX/pulsewatch.git /opt/pulsewatch
cd /opt/pulsewatch
cp .env.prod.example .env
chmod 600 .env
openssl rand -hex 24          # use the output as POSTGRES_PASSWORD
nano .env                     # fill in every value
```

`.env` holds every secret (database password, SMTP password). It is git-ignored; never commit it. The containers
receive these values as environment variables; nothing is written into images.

## 3. First start

```bash
docker compose -f docker-compose.prod.yml build          # builds the api, worker and migrations images
docker compose -f docker-compose.prod.yml up -d db
docker compose -f docker-compose.prod.yml run --rm migrate
docker compose -f docker-compose.prod.yml up -d
sudo ./deploy/egress-firewall.sh apply                   # §6 - do not skip
```

Check it:

```bash
docker compose -f docker-compose.prod.yml ps             # all services "Up", db "healthy"
curl -s https://status.example.com/health/ready          # Healthy
```

Caddy gets the certificate on the first request; that can take a few seconds.

## 4. Create your account

Registration is **closed by default**, because every account can make the server send requests.

1. In `.env` set `REGISTRATION_ENABLED=true`, then `docker compose -f docker-compose.prod.yml up -d api`.
2. Open `https://<your domain>/register` and create your account (and any teammates').
3. Set `REGISTRATION_ENABLED=false` again and run the same `up -d api`.

Existing accounts keep working while registration is closed.

## 5. Upgrades and migrations

Migrations run as a separate step, with an EF Core
[migration bundle](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying#bundles) built into the
`migrations` image. The API never changes the schema by itself.

```bash
cd /opt/pulsewatch
./deploy/backup.sh                                        # always back up first
git fetch --tags && git checkout v1.2.0                   # the release you want
sed -i 's/^PULSEWATCH_VERSION=.*/PULSEWATCH_VERSION=v1.2.0/' .env
docker compose -f docker-compose.prod.yml build
docker compose -f docker-compose.prod.yml run --rm migrate
docker compose -f docker-compose.prod.yml up -d
```

The old API and worker keep running during `migrate`. That is why migrations should stay backward compatible with
the previous release (add a column before code uses it; drop it only in a later release). Release notes say when
a migration is not.

**Rollback:** check out the previous tag, set `PULSEWATCH_VERSION` back, `build` and `up -d`. If the new release's
migrations are not compatible with the old code, restore the backup taken before the upgrade (§7).

Rebuild regularly (`build --pull`) even without a new release, so base images pick up security patches.

## 6. Worker egress firewall

This is SSRF protection layer 4 ([threat-model.md](threat-model.md#4-ssrf-controls-defence-in-depth)). The
application already refuses private addresses; the firewall makes sure that stays true even if the application
has a bug.

The worker is the only container on the `egress` network, whose bridge is named `pw-egress`. The script adds
rules to Docker's `DOCKER-USER` chain that reject traffic from that bridge to private, loopback, link-local
(including cloud metadata `169.254.169.254`), CGNAT, multicast and reserved IPv4 ranges, and to the host itself.
Public internet traffic (monitored sites, SMTP) is allowed.

```bash
sudo ./deploy/egress-firewall.sh apply
sudo ./deploy/egress-firewall.sh status
```

If your SMTP relay has a private address, allow just that address:
`sudo PW_EGRESS_ALLOW="10.0.0.25/32" ./deploy/egress-firewall.sh apply`.

**Test it.** A throwaway container on the egress network must reach a public site but not a private one or the
metadata address:

```bash
docker run --rm --network pulsewatch-prod_egress curlimages/curl:8.17.0 -s -o /dev/null -w "%{http_code}\n" https://example.com/
# 200
docker run --rm --network pulsewatch-prod_egress curlimages/curl:8.17.0 -s -m 5 http://169.254.169.254/ ; echo "exit $?"
# exit 7 (refused) or 28 (timeout)
docker run --rm --network pulsewatch-prod_egress curlimages/curl:8.17.0 -s -m 5 http://<a private IP on your network>/ ; echo "exit $?"
# exit 7
```

**Keep the rules after a reboot** with a systemd unit that runs after Docker:

```ini
# /etc/systemd/system/pulsewatch-egress.service
[Unit]
Description=PulseWatch worker egress firewall
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/opt/pulsewatch/deploy/egress-firewall.sh apply
ExecStop=/opt/pulsewatch/deploy/egress-firewall.sh remove

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload && sudo systemctl enable --now pulsewatch-egress
```

The rules match the bridge name, so they work before the worker container exists. If you changed
`EGRESS_SUBNET` or run on a host that uses nftables directly instead of Docker's iptables integration, adapt the
rules accordingly.

## 7. Backups

`deploy/backup.sh` writes a compressed `pg_dump` to `backups/` and keeps 14 days. Schedule it nightly:

```bash
crontab -e
# 15 3 * * * cd /opt/pulsewatch && ./deploy/backup.sh >> backups/backup.log 2>&1
```

**Copy backups off the server** (for example `restic` or `rclone` to object storage). A backup on the same disk
does not survive losing that disk.

**Test restores** after setting up backups and then monthly. This restores the newest dump into a scratch
database, prints row counts and removes it; the live database is not touched:

```bash
./deploy/restore-test.sh
```

**Real restore** (replaces all data):

```bash
docker compose -f docker-compose.prod.yml stop api worker
docker compose -f docker-compose.prod.yml exec -T db sh -c 'dropdb -U "$POSTGRES_USER" "$POSTGRES_DB" && createdb -U "$POSTGRES_USER" "$POSTGRES_DB"'
docker compose -f docker-compose.prod.yml exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --exit-on-error' < backups/pulsewatch-YYYYMMDDTHHMMSSZ.dump
docker compose -f docker-compose.prod.yml run --rm migrate   # only needed if the dump is from an older release
docker compose -f docker-compose.prod.yml up -d
```

Also keep a copy of `.env` and the `dataprotection-keys` volume somewhere safe. Without the keys, everyone has to
sign in again (no data is lost).

## 8. Logs and monitoring

* Logs are JSON on stdout, one object per line, rotated by Docker (5 × 10 MB per container):
  `docker compose -f docker-compose.prod.yml logs -f worker`. Monitor URLs appear as scheme and host only.
* `GET /health/live` (process up) and `GET /health/ready` (database reachable and migrated) answer
  `Healthy`/`Unhealthy` with no details. Point an outside uptime checker at `/health/ready`.
* The dashboard shows a "worker offline" banner when the worker's heartbeat is older than 60 seconds.
* Metrics and traces: set `OTLP_ENDPOINT` to an OpenTelemetry Collector. Metric names are listed in the README.

## 9. Configuration reference

Values in `.env` (see [.env.prod.example](../.env.prod.example)) are passed to the containers by
`docker-compose.prod.yml`. Any other setting can be added under a service's `environment:` with ASP.NET Core
naming (`Section__Key`).

| `.env` variable | Default | Used by | Meaning |
|---|---|---|---|
| `PULSEWATCH_DOMAIN` | — (required) | Caddy, worker | Public DNS name; also the base of links in emails |
| `ACME_EMAIL` | — (required) | Caddy | Let's Encrypt account email |
| `POSTGRES_PASSWORD` | — (required) | db, api, worker, migrate | Letters and digits only |
| `POSTGRES_DB` / `POSTGRES_USER` | `pulsewatch` | all | Database name and user |
| `REGISTRATION_ENABLED` | `false` | api | Allow new sign-ups |
| `MAX_MONITORS_PER_USER` | `20` | api, worker | Monitor quota per account |
| `EMAIL_PROVIDER` | `Smtp` | worker | `Smtp` or `Logging` (alerts only in the log) |
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_STARTTLS` | —, `587`, `true` | worker | SMTP server; port 465 always uses TLS |
| `SMTP_USERNAME`, `SMTP_PASSWORD` | empty | worker | SMTP login, if required |
| `EMAIL_FROM` | — | worker | Sender address for alerts |
| `OTLP_ENDPOINT` | empty (off) | api, worker | OpenTelemetry Collector (gRPC) |
| `PULSEWATCH_VERSION` | `local` | build | Image tag; handy for rollbacks |
| `EDGE_SUBNET`, `EGRESS_SUBNET` | `172.30.10.0/24`, `172.30.20.0/24` | networks | Change only if they clash |
| `HTTP_PORT`, `HTTPS_PORT` | `80`, `443` | Caddy | Host ports |

Settings with sensible defaults that you can override (examples): `Monitoring__MinimumIntervalSeconds` (60),
`Monitoring__MaxConcurrentChecks` (10), `Monitoring__CheckRetentionDays` (30), `Monitoring__AllowedPorts__0..n`
(80, 443, 8080, 8443), `RateLimiting__AuthPermitsPerMinute` (10), `Notifications__MaxAttempts` (6).

## 10. Limits of this setup

* **Run one worker** until you have measured a need for more. Several workers are safe (checks and emails are
  claimed with `FOR UPDATE SKIP LOCKED` leases, covered by the two-worker integration tests), but one handles
  hundreds of monitors at a 60-second interval ([load-test.md](load-test.md)).
* Checks run from one location, so a network problem near the server looks like an outage everywhere.
* Alert emails are delivered at least once: in rare cases (worker stops right after the SMTP server accepted a
  message) an email is sent twice.
* No high availability: the VM, Docker or PostgreSQL going down stops monitoring until it is back.

## Public demo

Don't expose a PulseWatch instance as an open URL checker. A demo must have `REGISTRATION_ENABLED=false`, a
seeded account with monitors pointing only at targets you own, a low `MAX_MONITORS_PER_USER`, and the egress
firewall applied.
