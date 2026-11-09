# Stock Purchase Split Payment

## Agreed Behavior

One purchase keeps one goods list and one history entry. Cash and cashless legs
sum to the goods total; they never split quantities. At100som for one bottle,
cash80/bank20 still receives exactly one bottle. No rounding adjustment entry.

Phone purchase form:total,cash,bank,local Refresh,Pay. Cash initially covers the
total;bank starts as an empty editor with a gray0hint. Both legs complement each
other immediately. Valid integer input above total clamps to total;invalid,
negative,fractional,overflow or incomplete required goods fields cannot submit.
Changing quantity/price updates line and basket totals without a full render.
Refresh recalculates locally without a Firebase read/command or resetting input.

Raw quantity/price/payment text and a stable operation UUID survive rerenders,
catalog expansion and saved-instance recreation. Drafts are scoped per club.
Cart stays until an applied command matches its UUID;send/status failure retains
it. This is UI saved-instance state,not a new durable offline-command queue or a
guarantee after force-stop/data-clear. Existing command delivery remains in use.

## Contract And Posting

Full PC snapshot advertises supportsStockPurchaseSplit=true. Updated phones use
ConfirmStockPurchaseSplit with purchaseOperationId,cashAmount,cashlessAmount and
the single purchaseItems array. Without the capability,mixed payment is rejected
locally;cash-only/bank-only keep the legacy path. Old PCs cannot silently ignore
split fields because the new command type is separate. Public PC rollout is
still required;installing the phone APK alone does not enable mixed payment.

StockPurchasePostingPolicy validates before mutation. A durable purchase intent
is atomically saved first. Inventory quantity and purchase markers are saved in
the same product_stock transaction. Incoming history and cash rows use stable
IDs;both cash legs link to StockPurchaseId. Retry/restart cannot repeat the same
receipt or leg. A changed payload for an existing UUID is rejected. Pending
operations recover on startup and existing owner-thread financial preparation/
purchase boundaries,never from a new timer or a background read constructor.
Unfinished preparation is not published as a successful fresh snapshot.

History exports ID and nullable cash/bank amounts in current/monthly sections.
Legacy unknown amounts stay unknown;no invented cash label or financial repair.
StockPurchaseStorage and incoming history now use existing atomic file writes.
Product cost remains the existing weighted-average rule. Purchases remain
excluded from operating-expense/pace deductions;the two cash rows sum once.

## Verification November9

Full Release PC regression suite passed,including32 new split checks,partial-
write/restart replay at six boundaries,weighted stock,legacy history and exact
cash expectation. Normal solution build0warnings/0errors. Mock background/
snapshot checks passed;no working-PC or production financial operation ran.

Android146unit tests pass;testDebugUnitTest/lintDebug/assembleDebug and test APK
build pass,lint0errors72warnings1hint. Native synthetic test passes on SM-S908N:
typing/pasting1/123/0 in all four real fields,clear/refocus,saved values,instant
totals,local Refresh preserving raw text/caret,recreation and320dp/1.3x layout.
First two device runs failed at the rapidly refocused bank field;waiting150ms
after focus for the Android input connection fixed the test. No production
input bypass or parser weakening was added. Final test uses native key input.

In-place debug1.4.6(146) installed on the single authorized phone;local/base APK:
e183d0bdfb5e43c5e3d8a2f945e527804a5107aa8f112b88666a506b1d52bc53.
GamePass encrypted records hash3fcb2139...unchanged. Phone screen observed before
inputs;warned bounded tests/repeats. Actual empty-cart purchase screen inspected
visually;left open without adding goods or paying. Real combined phone-to-club
posting/offline recovery remains untested;native Pay callback was synthetic.
The nonexported test host exists only in debug source,not release APKs.

## Traffic Boundary

No new Firebase service,path,listener,timer or frequency. Local Refresh and input
edits cause zero command callbacks. Pay uses the existing single-command PUT,
bounded status checks and existing result snapshot flow. Existing idle,dedup,
offline/restart and scope mock tests pass;no production load test was run.

Synthetic serializer sample:98bytes for the new command fields and81bytes per
history row including braces. At100rows,about8.1KB per history section;current
plus18 populated monthly sections could add about154KB to a full snapshot.
At3clubs/10full reads perclub/day for30days,this conservative extra history
budget is about139MB/month before protocol overhead;actual populated history,
read count and fanout matter.100purchases/day would add about9.8KB/day of command
fields perclub,before status/snapshot traffic. These are estimates,not live
measurements or guarantees of free usage. Plan/quota/headroom were not audited
for this local contract change;no billing/deployment action is authorized.

## Next Stage

Manual goods ordering in the admin PC acceptance list is a separate owner topic;
it was not implemented here. No release,tag,push or Firebase stable change.
