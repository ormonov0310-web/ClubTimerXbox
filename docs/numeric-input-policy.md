# Numeric entry and keyboard policy

## Empty editing text

- A new numeric input whose business default is zero starts with empty `Text`.
  Apply `NumericInputHint.Apply(input, persistentLabel)` to draw a gray zero.
  XAML may use the attached `NumericInputHint.Enabled` property instead.
- The hint is an adorner, not input content, and remains visible while an empty
  input has focus. It is excluded from selection, copying, parsing and storage.
- Do not insert zero on load, focus, each keystroke, clearing or binding refresh.
  Select-all on focus is not a replacement for an empty input.
- Preserve explicitly entered zero and existing saved values. PINs, codes, phone
  numbers and other identifiers must not undergo numeric normalization.
- Keep the domain's existing sign, precision, range and decimal-separator rules.
  Validate at confirmation; an empty mandatory amount is not silently saved as
  zero. An empty optional cash/bank leg explicitly means zero for KKM splitting.

## Keyboard-ready dialogs

- Use `DialogKeyboard.Configure(window, initialInput, confirmButton)` for these
  entry dialogs. It establishes logical focus, retries actual keyboard focus when
  the window is activated/rendered, and stops refocusing after initial success.
- The confirmation button is the default. Enter in the initial input consumes
  the key before raising the existing button click, ignores held-key repeats and
  respects a disabled button. Do not attach a second Enter handler that posts
  the same operation independently.
- Code dialogs for shift change, statistics and personal salary access focus the
  password input. Salary/debt repayment focuses the amount. Flexible time starts
  empty and focuses the amount, not minutes. KKM focuses the cash split field.
- Keep existing authentication, error handling, fresh-balance checks and payout
  idempotency. These helpers are UI behavior only, not transaction safeguards.

## KKM invariant

The two channels still sum to the checkout total. At a total of 150, entering
100 cash calculates 50 bank and vice versa. Empty/zero in one leg leaves the
whole total in the other. Typing/pasting a valid integer above the total clamps
it to the total; malformed, negative, fractional and overflow paste is rejected.
Updating the other leg must not rewrite the active editor or move its caret on
ordinary input. Confirmation rechecks the displayed values.

## Verification

Run from the canonical root, using normal build output:

```powershell
dotnet run --project ClubTimerXbox.RegressionTests -c Debug -- --numeric-input
dotnet run --project ClubTimerXbox.RegressionTests -c Debug --no-build
dotnet build ClubTimerXbox.slnx -c Debug --no-restore
```

The numeric suite exercises WPF controls with routed text composition, editor
commands, clipboard paste and preview Enter, not just final-value parsing. It
shows isolated offscreen windows with the actual theme and fake salary/debt
availability. The KKM confirmation test only creates an in-memory payment record;
it does not run the app, authenticate an employee, post money or access Firebase.
Additional storage-backed screens are inventoried by source checks rather than
opened against real club data. Full manual checkout/payout testing is distinct
from these isolated tests and must not be claimed without being performed.
