\timing on
-- 10 users, 50 monitors each (500), 2000 checks per monitor over 30 days (1,000,000), ~2 % failures.
INSERT INTO users (id, created_at_utc, user_name, normalized_user_name, email, normalized_email, email_confirmed,
                   security_stamp, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count)
SELECT 'user-' || u, now(), 'perf' || u || '@example.com', upper('perf' || u || '@example.com'),
       'perf' || u || '@example.com', upper('perf' || u || '@example.com'), false, md5(u::text), false, false, true, 0
FROM generate_series(1, 10) AS u;

INSERT INTO monitors (id, owner_user_id, name, url, is_enabled, interval_seconds, timeout_ms, failure_threshold,
                      recovery_threshold, expected_status_min, expected_status_max, state, consecutive_failures,
                      consecutive_successes, last_checked_at_utc, last_outcome, last_status_code, last_duration_ms,
                      next_check_at_utc, created_at_utc, updated_at_utc)
SELECT gen_random_uuid(), 'user-' || (1 + (m % 10)), 'Monitor ' || lpad(m::text, 3, '0'), 'https://api' || m || '.example.com/health', true,
       60, 10000, 3, 1, 200, 299, 1, 0, 10, now() - interval '30 seconds', 0, 200, 120,
       now() + (m % 60) * interval '1 second', now() - interval '31 days', now() - interval '31 days'
FROM generate_series(1, 500) AS m;

INSERT INTO monitor_checks (id, monitor_id, started_at_utc, completed_at_utc, duration_ms, outcome, http_status_code, error_summary)
SELECT gen_random_uuid(), mon.id, t.started, t.started + interval '120 milliseconds', 80 + (random() * 200)::int,
       CASE WHEN random() < 0.02 THEN 1 ELSE 0 END, 200, NULL
FROM monitors mon
CROSS JOIN LATERAL (
    SELECT now() - interval '30 days' + (g * interval '1296 seconds') + (random() * interval '60 seconds') AS started
    FROM generate_series(0, 1999) AS g) t;
UPDATE monitor_checks SET http_status_code = 503, error_summary = 'Unexpected HTTP status 503.' WHERE outcome = 1;

-- One resolved incident per monitor, and 20 open ones, each with a sent email.
INSERT INTO incidents (id, monitor_id, status, first_failure_at_utc, opened_at_utc, resolved_at_utc, failure_count,
                       last_failure_at_utc, opening_outcome, opening_status_code)
SELECT gen_random_uuid(), id, 1, now() - interval '10 days', now() - interval '10 days' + interval '3 minutes',
       now() - interval '10 days' + interval '20 minutes', 5, now() - interval '10 days' + interval '15 minutes', 1, 503
FROM monitors;
INSERT INTO incidents (id, monitor_id, status, first_failure_at_utc, opened_at_utc, failure_count, last_failure_at_utc,
                       opening_outcome, opening_status_code)
SELECT gen_random_uuid(), id, 0, now() - interval '5 minutes', now() - interval '2 minutes', 3, now(), 1, 503
FROM monitors ORDER BY id LIMIT 20;
UPDATE monitors SET state = 3 WHERE id IN (SELECT monitor_id FROM incidents WHERE status = 0);

INSERT INTO notification_outbox (id, incident_id, event_type, channel, recipient, idempotency_key, status, attempt_count,
                                 next_attempt_at_utc, last_attempt_at_utc, sent_at_utc, created_at_utc)
SELECT gen_random_uuid(), i.id, 0, 0, 'perf@example.com', i.id::text || ':IncidentOpened:Email:PERF@EXAMPLE.COM', 2, 1,
       i.opened_at_utc, i.opened_at_utc, i.opened_at_utc, i.opened_at_utc
FROM incidents i;

-- A long-running system: 100,000 emails already sent (kept forever) and 5 waiting.
INSERT INTO notification_outbox (id, incident_id, event_type, channel, recipient, idempotency_key, status, attempt_count,
                                 next_attempt_at_utc, last_attempt_at_utc, sent_at_utc, created_at_utc)
SELECT gen_random_uuid(), i.id, 1, 0, 'r' || g || '@example.com', i.id::text || ':IncidentResolved:Email:R' || g, 2, 1,
       now() - interval '20 days', now() - interval '20 days', now() - interval '20 days', now() - interval '20 days'
FROM generate_series(1, 100000) AS g
JOIN LATERAL (SELECT id FROM incidents ORDER BY id OFFSET (g % 500) LIMIT 1) i ON true;
UPDATE notification_outbox SET status = 0, sent_at_utc = NULL, next_attempt_at_utc = now() - interval '1 second'
WHERE id IN (SELECT id FROM notification_outbox ORDER BY id LIMIT 5);

INSERT INTO worker_heartbeats (worker_id, started_at_utc, last_seen_at_utc, version) VALUES ('perf', now(), now(), '1.0');
ANALYZE;
SELECT (SELECT count(*) FROM monitors) AS monitors, (SELECT count(*) FROM monitor_checks) AS checks,
       (SELECT count(*) FROM incidents) AS incidents, (SELECT count(*) FROM notification_outbox) AS emails,
       pg_size_pretty(pg_total_relation_size('monitor_checks')) AS checks_size,
       pg_size_pretty(pg_database_size(current_database())) AS db_size;
