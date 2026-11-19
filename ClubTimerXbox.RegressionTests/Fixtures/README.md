# Employee recovery fixture

SalikhEmployeeRecovery.json is a minimized synthetic storage envelope based on
the hash-verified SAL-EMP-RENAME-002 incident reviewed on 2026-11-20.
Source archive SHA256: 6fdaaa81c53b1feb0c1280e7ba3af66f63fe1c7a422cbfe2d5e5f91df56b38e1.
Only the exact incident guard anchors needed by the production repair are
retained: relevant IDs, pinned amounts/splits, timestamps, original derived
rating and wrong time checkpoint. Unrelated operations/real payroll are removed;
the current time checkpoint, manual rating and old payroll are synthetic.
No credentials,
PINs, bank-card details or authentication files are included.

The original private wrappers are NOT committed or complete application stores.
The test uses minimal document envelopes. Cash Description
contains only the exact structured salary-payment markers, not the original
human text. The July archive contains synthetic payroll, not an actual employee
balance or a verified full closed-month archive. Tests establish preservation of
unrelated history, not correctness of a past balance or complete current salary.

Run --employee-recovery. It uses only owned JSON and GUID-scoped temporary
directories, never the actual AppData-backed business services. Interrupted writes
are injected exceptions, not physical power-loss or disk durability tests.
