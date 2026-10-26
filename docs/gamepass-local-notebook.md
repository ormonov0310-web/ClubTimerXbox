# Local Game Pass notebook

Owner Android: Settings -> Game Pass subscriptions. The main bottom menu still
has six tabs. This is a local editable reference, not sign-in, banking, payment
integration or a cash/payroll/pace operation.

Records include clubs, monthly renewal anchors, device/account/pair/card aliases,
one common billing address and one shared JPY subscription price. Price changes
apply to every account; club editing has no independent price field.

Optional card holder/number/expiry/CVC are local notes. Numbers/codes remain
strings, including leading zeros. The owner chose no extra authentication prompt.
Storage is transparently AES-256-GCM with fresh nonces, an AndroidKeyStore key and
AtomicFile in noBackupFilesDir/gamepass-local-v2.bin. Crypto/file work runs off
the Activity UI thread. Failed decryption preserves the file; it does not reset
the key or silently replace records. This is not a payment-vault certification
or protection against an unlocked, compromised or debug-accessible phone.

Card secrets/address never enter notifications,network requests,logs or ordinary
record exports. Public import preserves existing local card secrets. Personal
fixtures/screenshots stay in ignored private notes, never compiled assets or
release files. Actual secrets must be entered only on the phone, not in chat.
An export is deliberately not a complete backup of sensitive fields.

One scoped inexact AlarmManager reminder per club is scheduled five calendar
days before monthly renewal around10:00 Asia/Bishkek. Android can delay it.
Boot/package/time broadcasts restore only these alarms. A bounded sent-cycle
ledger prevents duplicate real notices; a preview does not consume a cycle.
The notification opens the local Activity directly without loading Firebase.
Force-stop,uninstall and system notification restrictions still apply.

The only new network request is a public GET to
https://www.nbkr.kg/XML/weekly.xml. JPY is in NBKR's weekly feed; the app checks
daily but the published rate need not change daily. Value/Nominal is calculated
with BigDecimal. The parser rejects DTD/entities,invalid/nonpositive rates and
payloads above64KiB. Published and fetched dates are separate. The result is an
approximation, not a guarantee of bank conversion,fees or future charges.

WorkManager2.11.2 preserves minSdk23 (2.12 requires24). Unique daily and initial/
stale-view jobs use connected-network constraints and a synchronized daily success
guard. Errors retain the previous quote with bounded backoff and at most3attempts
per day. Queued work resumes on connectivity. No Firebase polling/listener,
cloud schedule,API key or paid service was added. Successful response bodies are
bounded to about1.9MiB/month/device at30fetches,excluding TLS/headers/failures and
existing app traffic. Actual aggregate/carrier costs were not audited.

Verification October26:95unit tests pass;testDebugUnitTest,lintDebug,assembleDebug
succeed. Lint0errors/69warnings/1hint. Device APK hash matches local output:
514d5f6de8b29af7a14b2abf2c0052745fc778b26e5d65de1b601787f4437900.
Installed-r with data preserved,1.4.6(146),19:11:35+06. Both clubs/11records/common
address and shared price persist on reopening. Native typing/copy/paste/clear/
refocus/explicit0 paths were exercised, including the shared-price placeholder.
Card dialog controls/actions fit the viewport; CVC/number are masked. No actual
card secret was collected during verification. The OS lists both reminder
targets; one preview was posted and inspected by its own package/tag. The live
rate downloaded once and repeated opens retained the same attempt/fetch state.
Offline cache/recovery is tested synthetically, not by changing phone network
settings. Actual reboot/next-month delivery and long-duration stability remain
unverified. This APK was installed locally; no Android public release performed.

References:
- https://www.nbkr.kg/index1.jsp?item=1562&lang=RUS
- https://developer.android.com/develop/background-work/services/alarms
- https://developer.android.com/privacy-and-security/keystore
- https://developer.android.com/jetpack/androidx/releases/work
