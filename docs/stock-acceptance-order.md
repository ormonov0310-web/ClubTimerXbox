# Manual Goods Acceptance Order

Local PC implementation, November 9, 2026. Not published or installed on club PCs.

## Owner Rule

Only the PC employee goods-acceptance list has a manually retained order. Hold
and drag only its six-dot grip on the left,then release above/below another
available product. A slightly enlarged,88%-opaque card follows vertically with
small inertial lag;neighbors yield when the leading edge reaches their midpoint:
bottom edge while moving down,top edge while moving up. No shadow/placeholder
remains on its original slot,and horizontal mouse motion does not move it. Quantity
inputs, buttons and other interactive controls cannot start a drag.

The first successful move establishes the manual order. Future popularity
changes do not override it. Before that move the normal popularity order applies.
New products append after the known products using popularity/name order.

Available means positive program stock. An unavailable product yields visible
places to available goods but keeps its reserved priority slot. Restocking
restores it to that slot, even if other available goods were rearranged during
its absence. Existing zero-stock 24-hour folding, negative-stock visibility and
draft protections remain in effect. Other lists keep their existing popularity
sorting. Renaming a product preserves its place because the key is its ID.

## Implementation

- `Services/StockAcceptanceOrderPolicy.cs`: pure priority, availability grouping
  and moves among available slots without overwriting absent products' slots.
- `Services/StockAcceptanceOrderStore.cs`: atomic local preference file
  `%APPDATA%\ClubTimerXbox\stock_acceptance_order.json`, schema 1, club ID to
  ordered product GUIDs. Reads return copies; invalid/incomplete/future files
  are not overwritten. A failed save leaves the displayed order unchanged.
- `StockAcceptanceDragBehavior.cs`: same-list payload, standard drag threshold,
  before/after insertion and edge autoscroll. An 80-ms timer runs only during
  an active drag near an edge; leave/drop/cancel/unload/dispose stops it.
- `StockAcceptanceDragVisuals.cs`: one raster snapshot per gesture,lift1.035,
  opacity0.88 and fixed X. A temporary CompositionTarget.Rendering handler follows
  targetY with70-ms exponential response;it unsubscribes on convergence,drop,
  cancel or disposal. Virtual reserved order changes only when the floating
  leading edge reaches a neighbor's reserved midpoint,including the lifted scale.
  No extra center-distance threshold or6DIP delay remains. Neighbors
  ease for180ms. Drop freezes the already chosen slot without sampling a new
  pointer destination;the floating card lands there in190ms. Preview changes
  only transforms,not physical child order or preferences. Cancel returns to the
  original layout;all transient visuals/clocks clear after200ms or immediately
  on unload/dispose. Windows animation preference respected;no idle polling.
- Target leave pauses edge scroll only. It cannot cancel a held gesture or
  trigger repeated snapshot/lift/return cycles. A transparent list background
  keeps gaps hit-testable and is restored on disposal. Explicit Escape/cancel,
  drop and actual DoDragDrop completion own gesture termination. A finishing
  gesture cannot restart from late hover events.
- `StockAcceptanceRowView.cs`: production card composition with a grip and
  wrapped labels, accepting the existing quantity/difference controls. Grip is
  a28x36 transparent hit region with the six-dot glyph,vertical cursor,tooltip
  and accessible name,marked by IsDragHandle attached property. Only that region
  and its descendants arm a drag;card/name/inputs remain ordinary interaction.
- `StockItemListPanel.cs`: opt-in priority, moving existing controls rather than
  rebuilding inputs. Last applied order survives paused sorting during keyboard
  entry. Other screens do not opt in and retain their prior behavior.
- `StockAuditWindow.cs`: captures the club scope, loads preferences once, saves
  only a changed completed drop, and rejects a drop if the club scope changed.
- `StockPurchasePostingPolicy.CommitIncomingInventory`: commits the already
  persisted incoming stock into existing objects so open lists keep observing
  those objects after the new purchase path replenishes a product.

Reordering does not accept goods, post cash, change stock, rewrite popularity,
clear typed quantities, or rebuild the audit. A drop saves only preferences.
An unavailable card cannot be dragged. Hover, cancellation and no-op do not save.

## Traffic And Scope

This feature adds no Firebase field, request, listener or polling cadence. It
loads one local preference at list creation and writes once per changed drop.
The existing local one-second grouping timer is unchanged; no idle file writes
or cloud work are added. Incremental Firebase operations/bytes: zero by source
inspection; no production traffic measurement or billing audit was performed.

No phone changes or installation were required. No release, helper dispatch,
real financial/stock-data edit, real StockAuditWindow startup or desktop takeover
was performed.

## Verification

- `dotnet run --project ClubTimerXbox.RegressionTests/ClubTimerXbox.RegressionTests.csproj -c Release -- --stock-acceptance-order`:
  62 checks passed with pure products, temporary preference files and isolated
  offscreen WPF controls.
- Tests cover default popularity/independent warehouse and procurement sorting,
  saved scope/reopening/rename/new/deleted IDs, absent reserved slots and restock,
  atomic preference guards, real routed WPF hover/drop/rejection behavior,
  unchanged typed text/selection, failed save, foreign-list payload, existing
  24-hour grouping and stock-object identity after purchase commit.
- Motion checks include weighted follow,vertical lock,opacity/lift,continuous gap
  hit testing,midpoint before/after behavior,repeated child leave/re-enter,
  frozen reserved-slot landing despite different release coordinates,and cleanup.
  Grip checks cover actual routed mouse-down arming on the glyph versus card,
  name/input rejection,and zero-stock grip. Leading-edge checks verify handoff
  occurs before the floating center reaches the neighbor's midpoint.
  The held-gesture regression failed against the previous implementation
  before that fix. Latest full-run Windows animation:true;sample followingY90.02
  DIP,final neighbor134.56DIP. Isolated controls,not working-PC timings.
- Rendered motion-before/during/preview/landing frames were inspected; captured
  preview differs in at least1000pixel-channel values from the resting list.
  Frames live beside the row renders in the private evidence folder.
- Full freshly rebuilt Release regression suite passed, including 32 mixed
  purchase checks, stock-folding/visibility, 19 numeric UI scenarios, financial
  and snapshot/close delivery tests.
- `dotnet build ClubTimerXbox.slnx -c Release --no-restore`: zero warnings/errors.
- Long Russian production-row renders inspected at 100%/150%, 800 physical
  pixels wide: complete wrapped text and quantity field without clipping.
  Private evidence: `.codex/local-notes/acceptance-order-2026-11-09/row-1.png`
  and `row-1,5.png`.

The owner reported the first motion version looping with a leftover shadow,and
specified vertical-only translucent weighted lift,midpoint handoff and automatic
landing in the reserved slot. This replaces the earlier motion design. New native
mouse/OLE animation,cancellation and edge
scrolling still require local interactive verification. Routed-event tests are
not proof of the physical gesture or deployment on a working club PC. The owner's
Debug app was observed running and was not stopped;only canonical Release outputs
were built. Restart the local Visual Studio run to load the new implementation.

Latest owner clarification:use bottom edge for earlier downward handoff,not the
initially written top edge. Upward behavior uses the leading top edge. Start only
from the six-dot grip,not the whole card. Reserved-slot landing and persistence
are unchanged. Revised native grip/edge feel still needs local owner checking.

Subsequent owner confirmation November9:current product dragging now works as
intended. Local drag UX is accepted. This does not prove working-club deployment,
independently describe every edge-scroll/cancel subcase,or authorize publication.

The framework event behavior was checked against the primary
[WPF DragDrop source](https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/DragDrop.cs):
target changes raise leave/enter,and OleDragLeave may use a synthetic zero point.
The application's old coordinate-based leave cancellation was therefore not a
valid indication that the mouse button had been released. No native trace of the
owner's exact gesture was collected;the source defect was reproduced via routed
events locally. The prior48 passing checks did not establish real mouse behavior.
