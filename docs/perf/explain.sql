\pset pager off
SELECT id AS mon FROM monitors ORDER BY name LIMIT 1 \gset
\echo '=== 1. Claim due monitors (scheduler, every 5 s) ==='
BEGIN;
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
UPDATE monitors AS m SET lease_until_utc = now() + interval '45 seconds'
WHERE m.id IN (SELECT id FROM monitors WHERE is_enabled AND next_check_at_utc <= now() + interval '30 seconds'
               AND (lease_until_utc IS NULL OR lease_until_utc < now())
               ORDER BY next_check_at_utc LIMIT 50 FOR UPDATE SKIP LOCKED)
RETURNING m.id, m.url, m.timeout_ms, m.expected_status_min, m.expected_status_max, m.next_check_at_utc;
ROLLBACK;
\echo '=== 2a. Check history page (monitor page) ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT * FROM monitor_checks WHERE monitor_id = :'mon' AND started_at_utc >= now() - interval '24 hours' AND started_at_utc < now()
ORDER BY started_at_utc DESC, id DESC LIMIT 20 OFFSET 0;
\echo '=== 2b. Check history total count, 30-day window ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT count(*) FROM monitor_checks WHERE monitor_id = :'mon' AND started_at_utc >= now() - interval '30 days' AND started_at_utc < now();
\echo '=== 3. Uptime and p95 for 24h / 7d / 30d (one pass) ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT count(*) FILTER (WHERE started_at_utc >= now() - interval '24 hours'),
       count(*) FILTER (WHERE started_at_utc >= now() - interval '24 hours' AND outcome = 0),
       percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_ms) FILTER (WHERE started_at_utc >= now() - interval '24 hours' AND outcome = 0),
       count(*) FILTER (WHERE started_at_utc >= now() - interval '7 days'),
       count(*) FILTER (WHERE started_at_utc >= now() - interval '7 days' AND outcome = 0),
       percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_ms) FILTER (WHERE started_at_utc >= now() - interval '7 days' AND outcome = 0),
       count(*), count(*) FILTER (WHERE outcome = 0),
       percentile_cont(0.95) WITHIN GROUP (ORDER BY duration_ms) FILTER (WHERE outcome = 0)
FROM monitor_checks WHERE monitor_id = :'mon' AND started_at_utc >= now() - interval '30 days' AND started_at_utc < now();
\echo '=== 4. Chart series, 30 days (180 buckets of 4 h), date_bin version ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
WITH agg AS (
    SELECT date_bin(interval '4 hours', started_at_utc, date_trunc('hour', now()) - interval '716 hours') AS bucket,
           count(*) AS checks, count(*) FILTER (WHERE outcome <> 0) AS failures,
           avg(duration_ms) FILTER (WHERE outcome = 0) AS avg_ms, max(duration_ms) FILTER (WHERE outcome = 0) AS max_ms
    FROM monitor_checks
    WHERE monitor_id = :'mon' AND started_at_utc >= date_trunc('hour', now()) - interval '716 hours'
      AND started_at_utc < date_trunc('hour', now()) + interval '4 hours'
    GROUP BY 1)
SELECT b.bucket, coalesce(agg.checks, 0), coalesce(agg.failures, 0), agg.avg_ms, agg.max_ms
FROM generate_series(date_trunc('hour', now()) - interval '716 hours', date_trunc('hour', now()), interval '4 hours') AS b(bucket)
LEFT JOIN agg ON agg.bucket = b.bucket ORDER BY b.bucket;
\echo '=== 5a. Dashboard: my monitors ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF) SELECT * FROM monitors WHERE owner_user_id = 'user-1';
\echo '=== 5b. Dashboard: my open incidents ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT count(*) FROM incidents i JOIN monitors m ON m.id = i.monitor_id WHERE m.owner_user_id = 'user-1' AND i.status = 0;
\echo '=== 5c. Dashboard: worker last seen ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF) SELECT max(last_seen_at_utc) FROM worker_heartbeats;
\echo '=== 6. Incident list (newest first) ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT i.*, m.name FROM incidents i JOIN monitors m ON m.id = i.monitor_id WHERE m.owner_user_id = 'user-1'
ORDER BY i.opened_at_utc DESC, i.id DESC LIMIT 25;
\echo '=== 7. Retention batch (oldest 5000 beyond 29 days, so it finds rows) ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
SELECT id FROM monitor_checks WHERE started_at_utc < now() - interval '29 days' ORDER BY started_at_utc LIMIT 5000;
\echo '=== 8. Outbox claim (100,005 rows, 5 due) ==='
BEGIN;
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF)
UPDATE notification_outbox AS o SET status = 1, attempt_count = o.attempt_count + 1, last_attempt_at_utc = now(), lease_until_utc = now() + interval '3 minutes'
WHERE o.id IN (SELECT id FROM notification_outbox
               WHERE status IN (0, 1, 3)
                 AND ((status IN (0, 3) AND next_attempt_at_utc <= now()) OR (status = 1 AND lease_until_utc < now()))
               ORDER BY next_attempt_at_utc LIMIT 20 FOR UPDATE SKIP LOCKED)
RETURNING o.id, o.attempt_count;
ROLLBACK;
\echo '=== 9. Outbox pending count (dispatcher, every poll) ==='
EXPLAIN (ANALYZE, BUFFERS, COSTS OFF) SELECT count(*) FROM notification_outbox WHERE status IN (0, 1, 3);
