# Query plan review

Milestone 7 checked that PulseWatch's frequent and user-facing queries stay fast with a realistic amount of data.

## Setup

- PostgreSQL 18.6 (the Docker Compose image) on a developer laptop, default settings.
- Data: 10 users, 500 monitors, **1,000,000 checks** spread over 30 days (about 2 % failures), 520 incidents and **100,520 notification rows**: a long-running system where sent emails pile up, because the outbox is never cleaned.
- Database size: 251 MB, of which the checks table and its indexes are 205 MB.
- Each query ran with `EXPLAIN (ANALYZE, BUFFERS)` after `ANALYZE`; writes ran inside a rolled-back transaction.

Reproduce: create an empty database, apply the migrations, then run [`perf/seed-1m-checks.sql`](perf/seed-1m-checks.sql) and [`perf/explain.sql`](perf/explain.sql) with `psql`. The full output is in [`perf/explain-results.txt`](perf/explain-results.txt).

## Results

| # | Query | Runs | Plan | Time |
|---|---|---|---|---|
| 1 | Claim due monitors (`UPDATE … FOR UPDATE SKIP LOCKED`) | every 5 s | Index scan on `ix_monitors_due` | 1.0 ms |
| 2a | Check history page | monitor page | Index scan on `ix_monitor_checks_monitor_started` | 0.3 ms |
| 2b | Check history total, 30-day window | monitor page | Bitmap index scan, same index | 12 ms |
| 3 | Uptime + p95 for 24 h / 7 d / 30 d, one pass | monitor page | Bitmap index scan, same index | 3.0 ms |
| 4 | Chart series, 30 days | monitor page | Bitmap index scan, same index, then `date_bin` | 2.2 ms (was 29 ms) |
| 5a–c | Dashboard summary: my monitors, my open incidents, worker last seen | dashboard | Index on `owner_user_id`; tiny tables | 0.1 ms each |
| 6 | Incident list | incidents page | Index on `owner_user_id` + hash join | 0.2 ms |
| 7 | Retention batch (oldest 5,000) | daily | Index scan on new `ix_monitor_checks_started_at` | 1.5 ms |
| 8 | Claim due emails | every 10 s | Index scan on `ix_notification_outbox_due` | 0.3 ms (was a full scan) |
| 9 | Count pending emails (metrics gauge) | every 10 s | Index-only scan, same partial index | 0.04 ms |

## Changes made because of this review

1. **Chart series rewritten with `date_bin`.** The first version joined the list of buckets to the checks by time range, which compares every bucket with every check: 180 × 2,000 = 360,000 comparisons for one monitor here, and about 7.8 million with real 60-second checks (43,200 per month). Grouping each check into its bucket with `date_bin`, then joining the bucket list to that small result, reads each check once: 29 ms → 2.2 ms. The existing exact-value tests passed unchanged.
2. **Email claim index widened.** The claim's `OR` (due Pending/Failed rows, or Sending rows whose worker died) didn't match the partial index, so every poll scanned the whole outbox, which only ever grows. The index now covers Pending, Sending and Failed, and the query starts with `status IN (0, 1, 3)` so the planner can use it. With 100,000 sent emails the claim reads 5 index entries.
3. **New index `ix_monitor_checks_started_at`** for retention, which deletes the oldest checks across all monitors. Without it, each 5,000-row batch would scan the 1 M-row table.

## Notes

- 2b (counting a full 30-day window) is the slowest query at 12 ms because one monitor's checks are spread over about 2,000 table pages: checks of all monitors are written interleaved in time. It only runs when someone opens a monitor's history with a wide window. If it ever matters, a covering index `(monitor_id, started_at_utc) INCLUDE (outcome, duration_ms)` would make 2b, 3 and 4 index-only. That isn't worth the extra write cost today.
- Incidents (6) are read with a sequential scan because the table is tiny. Incidents are never deleted, but they grow by a handful per monitor per month; `(monitor_id, opened_at_utc)` is indexed for when it matters.
