# Employee identity at startup

The persisted employee roster is authoritative whenever it is nonempty.
Default accounts are first-installation templates, not names that must exist
forever. Renaming or disabling a template-derived employee must not create a
replacement, change its permanent EmployeeId or restore its old credentials.
EmployeeRosterInitialization contains this pure selection rule; EmployeeService
still uses EmployeeStorageService for its existing ID normalization and save.
Existing duplicate profiles are not silently removed or merged by this change.

The historical Salikh Mirbek-to-Muhammed name-only repair is retired from
KnownDataRepairService. Its former guard checked the destination employee ID,
but then renamed every matching old-name reference at each startup. Another
employee could later own that name. Do not reintroduce a global name migration
as a startup repair; any further historical correction needs independently
verified source-record identities and an explicit, replay-safe scope.
The unrelated Salikh cash and Telecom repairs remain unchanged.

The prevention itself does not restore already misattributed operations.
The separate, owner-approved case-specific restoration is described below.
Neither change subtracts 132 som from an observed balance or moves every
Muhammed record to Mirbek.

## Inactive employees

An inactive account cannot start/switch to a shift, commit new game/product
activity, receive new owner bonuses, automatic rating events or new over-plan
portions. Existing frozen portion awards and historical earnings remain intact.
Disabling an active employee closes their open shifts at the disable time;
already closed intervals are never reopened or extended by this operation.
Checkout validates activity again after the modal and before stock/money changes.

Explicit owner exception: debt contracts and automatic repayments CONTINUE for
inactive debtors and creditors. Existing debt balances, repayment cursors,
credits, historical payouts and old obligations are not zeroed or paused.
Disabling an account is not a financial cancellation or deletion.

## One-time Salikh incident recovery

SalikhEmployeeRecoveryService runs before business services initialize, only for
club_2 / installation eccc4fab4273471c8ac4720cd6e96772. The exact November18/19
record IDs and permanent employee identities were independently checked in
SAL-EMP-RENAME-002; the owner corroborated that historical Muhammed no longer
worked there. Other clubs/installations have no recovery writes or process scan.

The owned JSON plan restores the closed shift, five games, four attached product
lines, eight KKM payments, fourteen cash rows, two acceptances and linked extra
responsibility. It preserves Test's true creator role. Financial amounts, payment
channels and operation IDs do not change; the three completed salary payouts
21+100+11 remain the same transactions, now correctly attributed in cash history.
The raw shift restores worked-time calculation; the verified wrongly attributed
November time checkpoint is cleared without adding carried hours to Mirbek.
Mirbek's current checkpoint is retained. Old July payroll and other months remain
untouched. The one derived reward is cancelled under the wrong identity and
replaced using Mirbek's base100, target105, original effective interval and a
stable source key. Other manual/time rating events remain unchanged.

All target sources are prevalidated before writes. Missing/duplicate/changed
records, another running ClubTimer, manual time recovery, resolved reward or a
started/closed November payroll block this incident repair instead of guessing.
No broad name search/reassignment or salary amount is invented.

The local repairs/salikh-employee-20261118-v1 directory contains original backups,
staged files and a durable prepared/committed journal with hashes. Existing .bak
fallback files move to the repaired generation too. A prepared operation resumes
before financial services; unexpected external changes block it. A committed
operation is skipped on later starts, even as normal business data evolves.
Credentials are not backed up by this recovery. Do not delete the repair journal
or overwrite backups to force a retry. Failure stops this launch before accounting
and requires review, not continuation with a partly repaired generation.

This is locally implemented and tested, NOT yet published or applied on Salikh.
The final current salary must be verified after authorized on-club execution;
the sanitized evidence does not independently prove an exact current balance.

## Verification

Run the regression executable with --employee-identity for 43 pure-data and
production-IL wiring checks. It covers initial seeding, renamed and disabled
profiles, partial custom rosters, existing duplicates, saved IDs/credentials and
20 serialized restart cycles. IL inspection checks the actual startup policy
call, absence of name reseeding/global rename and preservation of other repairs;
it does not initialize AppData-backed services or run their repairs.
--employee-activity adds43 pure-data, portion-engine and IL wiring checks.
--employee-recovery adds93 checks using a sanitized report-derived fixture and
temporary folders, including interruption after every replacement, restart,
conflict refusal and conservation. The fixture limitations are documented in
ClubTimerXbox.RegressionTests/Fixtures/README.md. These do not prove a physical
power-loss recovery, native UI workflow or a successful production repair.
All three groups run in --finance-headless and the full regression suite.

Normal application startup against real AppData is not a safe diagnostic test:
other existing startup services can persist financial state. Do not use it just
to verify these rules without the owner's explicit live-data approval.

No Firebase transport, send cadence, listener or retry policy changes. There are
no new timers, listeners, polling or external dependencies. The repair uses local
files once; ordinary existing automatic sync publishes the corrected business
state after an authorized real launch. No cloud operation is used to perform or
measure this repair. Native dialogs/numeric entry were not changed or exercised.
