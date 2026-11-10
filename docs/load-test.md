# Load test: 500 monitors on one worker

**Question:** can one worker with default settings keep 500 monitors on a 60-second interval, and what does it cost?

**Answer:** yes, easily. On a laptop the worker used about **5 % of one CPU core** and **177 MB** of memory, every check succeeded, and each monitor was checked every **59.9 s** on average. The run also found and fixed a scheduling drift (below).

## How it was run

```powershell
dotnet run --project tests/PulseWatch.LoadTest -c Release -- 500 5   # monitors, minutes
```

[`tests/PulseWatch.LoadTest`](../tests/PulseWatch.LoadTest/Program.cs) is a separate console tool, never shipped. It:

- starts a throwaway PostgreSQL 18.6 container (Testcontainers) and applies the migrations;
- starts a small local HTTP server that answers `200` after 20–80 ms, like a typical health endpoint;
- creates 500 monitors with default settings (60 s interval, 10 s timeout), **all due at the same moment**, the worst case (for example right after a deploy, or when 500 monitors are imported at once);
- runs the real worker with its default settings (poll every 5 s, batches of 50, 10 concurrent checks), reading lag, durations and counts from the `PulseWatch` meter, CPU from the process, and growth from `pg_database_size`.

To reach the local server, the tool swaps in a loopback-allowing address policy and DNS resolver through dependency injection, exactly like the integration tests. The product itself has no setting for this.

Machine: 12 logical CPUs, Windows 11, .NET 10.0.11; PostgreSQL in Docker Desktop on the same machine. Probes don't leave the machine, so these numbers measure PulseWatch, not the internet.

## Results (after the fix)

| | |
|---|---|
| Checks recorded | 2,650 in 5 minutes, **0 failed**; every monitor checked once per minute |
| Time between checks of one monitor | **average 59.92 s**, max 61.40 s (configured 60 s) |
| Worker CPU | **4.5 % of one core** on average (0.4 % of the machine) |
| Worker memory | 177 MB peak working set |
| Database growth | ≈ 340 bytes per check including indexes |
| Projected size at 500 monitors × 60 s × 30 days retention | ≈ **6.8 GB** of check history, held there by the daily cleanup |
| Probe duration | p50 57 ms, p95 87 ms (the local target itself takes 20–80 ms) |

**Lag** (how long after its due time a check was claimed), per minute:

| Minute | Checks claimed | p50 | p95 | Max |
|---|---|---|---|---|
| 1 | 500 | 2.27 s | 4.03 s | 4.03 s |
| 2 | 500 | 1.93 s | 3.59 s | 3.59 s |
| 3 | 500 | 1.89 s | 3.56 s | 3.56 s |
| 4 | 500 | 1.88 s | 3.58 s | 3.58 s |
| 5 | 500 | 1.94 s | 3.63 s | 3.63 s |

The minute-1 burst (500 checks due at once) drained in about 4 seconds: 50 claimed per batch, 10 probed at a time, about 80 ms each. After that, lag stays below one poll interval (5 s), which is the scheduling tolerance the README promises.

## What the first run found

The first run used the original rule: the next check is due **one interval after the check started**.

| | First run (old rule) | After the fix |
|---|---|---|
| Lag p50 in minutes 3–5 | 4.85–4.93 s | 1.88–1.94 s |
| Lag p95 in minutes 3–5 | 4.99–5.06 s | 3.56–3.63 s |
| Real interval | ≈ 65 s (inferred: lag added each cycle) | 59.92 s (measured) |
| CPU | 4.6 % of one core | 4.5 % of one core |

Because a check starts a little after it is due (the worker polls every 5 s), "started + 60 s" pushed every check later by its lag. The lag grew until checks waited almost a full poll interval, and a 60 s monitor really ran every ~65 s: about 8 % fewer checks than configured, and slower incident detection.

**Fix:** the next check is now due one interval after this check was **due** (`EndpointMonitor.CompleteCheck` takes the claim's due time), so the polling delay no longer adds up. If a check ran a whole interval late (for example after the worker was down), that late check counts as the catch-up and the next one is due an interval after it started: no burst and no immediate second check. Unit tests cover both cases. The first version of the fix scheduled an immediate catch-up instead, and the existing scheduler integration test caught the pointless double check it caused. The measured run above used that first version; it never reached the late-check path (lag stayed under 5 s), so the numbers are the same for the final rule.

## Limits and what this does not show

- **One location, local targets.** Real targets add network latency and timeouts. A probe that hangs until its 10 s timeout holds one of the 10 concurrent slots, so the worst case is 500 monitors that all time out: 500 × 10 s ÷ 10 slots = 500 s per round, far beyond 60 s. Many slow or dead targets need more concurrency (`Monitoring:MaxConcurrentChecks`), and the `pulsewatch.worker.lag` metric shows when that happens.
- **Five minutes.** Long-run effects (database bloat, retention cost) are covered by the [query plan review](query-plans.md), not this test.
- **One worker.** Several workers claim safely (see the concurrency tests), but the README still recommends one worker until you have tried more in your own deployment.
