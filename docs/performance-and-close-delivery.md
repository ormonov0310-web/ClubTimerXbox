# Local performance and normal-close delivery

## Employee journal lookup, October28

The October28 diagnostic tables identify repeated CashService.Records.Any in
EmployeeStatsService.AddGameSessionJournal during a full UI-thread snapshot build.
This is separate from the JSON encoding optimization in1.6.0. One observed build
took9958ms; the report attributes about6766ms of reconstructed sampled thread-time
to this Any leaf. Those samples are not exact kernel CPU time or call counts.

The local fix constructs a HashSet of payment-attributed game-session IDs once
per game-journal invocation,then performs membership lookups. The predicate is
unchanged:GameSession type,matching non-null session ID and present PaymentRecordId,
including Guid.Empty. It does not filter the index by employee,date,amount,payment
channel or closed state. Existing employee/month checks and all rows/order remain
in their original locations. The set is rebuilt each time;no global stale cache.
No financial service was moved to a worker thread.

An internal explicit-input overload permits pure fixture tests without opening
the application or initializing live stores. Tests compare all ordered row fields
and totals against the frozen1.6.0 implementation,cover enum/null/empty IDs,
duplicates,signed amounts,employees,month boundaries,sale/extra rows,randomized
inputs and successive changes. A counted enumerable asserts one cash traversal.
The4638-session/7377-record synthetic single-journal measurement was24.942ms
before and15.282ms after on this home machine,not club UI latency or a prediction
that the complete9.96s freeze is solved.

Focused journal checks,57 cash checks,cash-summary fixture checks and normal
Release project/solution builds pass. No windows were opened for these checks;
the owner is remote and another person uses the home PC. The full visual suite,
real-club responsiveness and shorter overview/OverNormPortionStore pauses remain
unverified. This is a local pending change,not a published update.
No Firebase request,listener,interval,payload schema or retry rule was changed.

## Cash headline

The once-per-second headline does not need detailed report rows. CashReportService
now provides a summary-only calculation using the same allocation, category,
refund and legacy sale-date rules as the detailed report. Session IDs are indexed
once per calculation, lazily: direct-source sale lines and game-only rows need
no session history. Duplicate legacy session IDs retain the first match.

MainWindow caches the daily headline by business date, PaymentService.Revision
and ActionLogService.Revision. The revisions are in-memory invalidation counters,
not accounting values, stored data or cloud revisions. Payment save/clear and log
save invalidate the result. Any future mutation API that bypasses these save
paths must also invalidate the corresponding revision. Do not cache only by
record count: corrections and legacy date changes can keep the same count.

Detailed report screens retain their rows/order. Firebase summary callers no
longer build unused UI rows. No financial rules, persisted records, tariff
calculation or payroll have changed.

## Critical close packet

Previously, the ordinary close event could be delivered independently before
the full final snapshot and presence update. A slow financial calculation or
expired deadline could leave the server's liveState open after a closed notice.
The connection monitor correctly warns after three minutes without a heartbeat
when isOpen remains true; increasing its timeout would hide genuine outages.

Ordinary close now saves local state and durable pending delivery, queues the
stable-ID close event, cancels normal publishing and submits one scoped multi-path
PATCH under the current club. The packet contains the event plus closed liveState
and overview fields. It does not recalculate monthly payroll/pace or replace cash
and history. Busy places become zero in presentation, not in local game state.
The full final snapshot is attempted after the critical packet, within the
existing deadline, and remains durably pending if unsuccessful. Do not acknowledge
the full snapshot merely because the critical close packet succeeded.

The current close event has a priority sender rather than waiting behind the
ordinary outbox. A process-local ID keeps the ordinary sender from sending that
same event alone. Failed records stay in the existing durable outbox. Presence is
not stored with old outbox records: replay after a later startup sends only the
historical event, never an old closed state over a new live session. After a
successful critical packet, do not publish redundant closed presence again.

Protocol reference: [Firebase REST multi-path updates](https://firebase.google.com/docs/database/rest/save-data#updating_data_with_patch).
No server rules, cloud functions, billing plan or deployed application was changed
for this local implementation. Source rules support these scoped paths; the
actual deployed write and notification flow still needs an authorized live check.

## Traffic boundaries

- No new listener, heartbeat, polling loop or timer was added. Existing command
  checks at2s and the90s liveness cadence are unchanged, not newly endorsed as a
  complete quota optimization.
- Normal close uses one critical PATCH rather than separate event/live/overview
  writes; the existing final snapshot attempt remains. With three clubs closing
  once per day, that is three critical packets/day, not a new idle workload.
- Estimated critical payload is a few KB for12-20 places and normal event/name
  lengths; actual bytes depend on names and event fields. This excludes existing
  final snapshot/history traffic and downstream notifications. Fan-out still has
  one event and one live-state transition per close to each enabled owner device.
- Failed close does not introduce a retry loop while exiting. The queued event
  resumes through the existing outbox after startup; full snapshot pending state
  remains durable. Do not replay stale presence during that recovery.
- Current account quotas/billing and whole-app traffic were not audited here.
  Smaller payloads and fewer calls are not a zero-charge guarantee.

## Detached snapshot preparation and stage timings

Financial services still run on their original caller thread. A complete owned
JsonObject is first created there. Only comparison of detached JSON and its
encoding run on a worker; never move BuildCurrentState or payroll/debt mutation
into Task.Run merely to hide a stall. The prepared UTF8 body is sent directly,
without first making a whole JSON string to count bytes and serializing it again.
The existing current-publish gate, pending generation, revision, acknowledgment,
backoff and offline recovery remain in effect. An old scope is checked before
sending prepared data, and a canceled request does not invoke the financial
builder. The close path uses the same prepared-byte transport.

The existing local `%APPDATA%\ClubTimerXbox\firebase_snapshot_delivery.json`
now records BuildMilliseconds, PrepareMilliseconds, SendMilliseconds,
BuildOnUiThread, BuildThreadId and PrepareThreadId (the IDs are managed .NET
thread IDs, not native Win32 thread IDs). Bounded BuildStages contain calls,
inclusive elapsed time and managed allocations on that synchronous thread:

- `snapshot.build-state`, `snapshot.history`, `snapshot.serialize-node`;
- `salary.month`, `salary.daily`, `salary.inputs`;
- `pace.month`, `pace.save`, `debt.auto`.

Do not add nested stage times/allocations together; parents include their children.
Elapsed time can include lock/file waits and is not CPU sample time. Thread-local
managed allocation counts do not measure native memory or other threads.
The latest full build >=250ms is retained separately in LastSlowBuild* so a later
fast build cannot immediately erase the useful sample. OverviewBuild and
LastSlowOverviewBuild measure the compact overview/heartbeat's financial work as
well. Overview recording changes only in-memory diagnostics; it is persisted at
the next already-existing delivery checkpoint, not through a new idle file write.

CheckpointMilliseconds separately measures the existing diagnostic-file save.
LastSlowCheckpoint* retains a save taking at least250ms. The completed save's own
duration is persisted only by the next ordinary checkpoint, never a second write.

At most16 fixed stage labels are captured per profile; no employee identifiers,
money values, credentials, exception text or business payload is logged. Metrics
stay out of the Firebase snapshot. No capture scope means the measurement helper
is inactive. No production timer, listener, scheduled task or cloud traffic was
added. The diagnostics are not a cash journal and do not change accounting rules.

The synthetic4.8MB baseline measured about155ms to create the JSON document and
about70ms/24MB of temporary managed allocation to prepare/count/encode the body.
Single UTF8 preparation measured about34ms/4.8MB after warm-up. These figures
cover a DTO fixture and pure salary/pace kernels, not AutoSalaryService.BuildReport
on a real club database. They do not explain or establish a3-5s Salikh freeze.
Real financial entry points are instrumented for their next natural invocation;
the test never initializes their live stores to benchmark them.

The300.085s WPF dispatcher fixture used four90s refresh attempts, one initial
4,807,714-byte fake send and zero redundant writes. It observed11,962 dispatcher
ticks and a maximum48.371ms gap on this machine. This is not the live monthly
financial builder, the full application or production network behavior.

## Verification and limitations

```powershell
dotnet run --project ClubTimerXbox.RegressionTests -c Debug -- --cash-performance
dotnet run --project ClubTimerXbox.RegressionTests -c Debug -- --close-delivery
dotnet run --project ClubTimerXbox.RegressionTests -c Debug -- --sync-idle
dotnet run --project ClubTimerXbox.RegressionTests -c Debug -- --snapshot-baseline
dotnet run --project ClubTimerXbox.RegressionTests -c Debug -- --snapshot-performance
dotnet run --project ClubTimerXbox.RegressionTests -c Debug -- --snapshot-ui-idle
dotnet run --project ClubTimerXbox.RegressionTests -c Debug --no-build
dotnet build ClubTimerXbox.slnx -c Debug --no-restore
```

Cash fixtures compare summaries and detailed rows, including negative payments,
rounding differences, mixed methods, legacy backdated sales, explicit sale IDs,
all revenue view modes and period modes. Cache tests cover300 unchanged seconds,
payment/log revision changes, the next business date and failed recalculation.

A synthetic7417-payment/4608-session Debug benchmark reduced the headline path
from about217ms to11ms on this development machine; unchanged refresh reused the
result. This is not measured Salikh UI latency or proof of the full freeze cause.
Close tests exercise the actual request builder/coordinator against an in-memory
server: event/state in one packet, delayed/full-snapshot failure, cancellation,
repeat ID and old-event replay. They are not Firebase emulator or production tests.

A real300.3s local mock snapshot idle interval produced one initial111-byte
application write and zero redundant writes. It does not measure all application
traffic, actual phone delivery, background/foreground behavior or server-side
late writes. Existing synthetic recovery/backoff tests pass. Full Debug regressions
and the normal solution build pass without warnings/errors.

Before claiming the club symptom solved, perform an authorized real work-club
responsiveness check and normal-close/three-minute alarm observation. The exact
method responsible for all periodic pauses still needs targeted measurement;
large month/pace/payroll snapshots remain a separate candidate. Those PC checks
did not change production data, a release or a work-PC installation. The separate
owner Android milestone follows below.

## Owner Android memory crash, October26

The connected phone's crash log confirms repeated OutOfMemoryError with a256MB
managed heap growth limit. Most preserved stacks reach ClubLiveStateStore's
mergeOverview via resume, live-state broadcasts, manual refresh or command refresh.
The latest recorded exception before the fix is17:24:02+06. Another stack fails
in readConnectionText while allocating a large response string; this reading path
is not rewritten by the narrow fix below.

The companion Android source uses SnapshotJson.copyObject for the root and each
branch changed by a live-state overlay. Untouched history remains shared and
read-only, not serialized and parsed again on each status update. Closed-place
projection copies its array/items before clearing presentation fields, preserving
the cached sessions. Older-download selection similarly avoids a full cache clone.
Future changes must copy any shared branch before mutating it. No history is
deleted or truncated; no largeHeap workaround, new API dependency or Error catch.

All76 Android unit tests pass, including6 new memory/copy-isolation cases.
testDebugUnitTest,lintDebug,assembleDebug succeeded; lint has0errors/67warnings
and1hint. The debug APK was installed-r on the single authorized SM-S908N,
version1.4.6(146),lastUpdate17:59:29+06. Device base.apk SHA256 matches the build:
892c358d1fe752ae3b5ec8c6fc8fcea4511eb1a49cd60ec7447e1115a789453c.
Launch and3 background/foreground transitions passed with the same process;
the overview screenshot was inspected and no new crash was observed in this
short check. This is not day-long stability or proof that every memory-pressure
path is solved. Large response parsing and concurrent loads remain follow-ups.

This change adds no network trigger,read/write,polling interval or listener;
automatic sync and the existing traffic counters remain unchanged. No cloud quota
or billing audit was performed; traffic savings are not claimed. Real cash,
commands,credentials and data were not cleared or manually edited. No public
release was performed for this device-development milestone.
