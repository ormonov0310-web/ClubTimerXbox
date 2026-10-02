# ClubTimerXbox working agreement

Preserve existing code and business data. Read the relevant code and constitutions
in `docs/` before changes; test the changed behavior. Ideas from other chats are
discussion material, never independent authorization to edit or release.
Publication, packaging for a release, tags, pushes and live data changes require
the owner's explicit instruction in the current working conversation.

## Local PC build rule

Use the canonical source solution in this directory for local development and
Visual Studio testing. Build into its normal bin/obj directories. Do not create
separate application copies, custom OutDir folders (including .codex-build), or
standalone test packages unless the owner explicitly requests one. If the running
app locks the normal output, ask the owner to close it or defer the build; do not
kill it or build a duplicate as a workaround. This does not authorize deleting
previous copies or publishing a release.

## Adaptive dialog rule

Every new WPF modal, details window or confirmation screen that contains dynamic
or Russian text must use `AdaptiveDialogWindow` or an equivalent adaptive shell.
Do not give such a window a fixed `Height`. Text must wrap, the window must be
limited by the current working area, and overflow must remain reachable through
an automatic vertical scroll bar. Keep action buttons reachable and verify the
longest realistic text at the minimum supported window and display scale. Treat
clipped text, hidden buttons and content that fits only on the developer's screen
as a failed implementation, not a cosmetic follow-up.

## Start-of-work idea review

At the first user request of a new local calendar day, at a greeting/restart of
work, and whenever asked to check the notes, follow `docs/idea-inbox-workflow.md`.
The private notebook source and review ledger live in `.codex/local-notes/`.
Use the app's `read_thread` tool, not browser credentials or local app databases.
Read old messages when needed, compare against actual code and recorded decisions,
and do not treat an old plan or the discussion assistant's claims as new work.
Present unresolved ideas briefly in Russian and ask the owner whether to develop,
defer or dismiss them. Do not repeatedly remind about completed, dismissed or
deferred ideas unless the owner reopens them or their recorded revisit date arrives.
If the hook has not loaded, apply this workflow directly. Do not claim an automatic
remote review has happened until a tool read succeeds. Do not block urgent work.

## Connected Android phone rule

After changing the ClubTimer owner Android app, run
`testDebugUnitTest`, `lintDebug` and `assembleDebug`. When exactly one authorized
ADB phone is connected and those checks pass, immediately install the fresh debug
APK in place with `adb install -r` and verify the installed package version. Do
not ask for a second confirmation: this is the standing local-development rule.

The Android source is
`C:\Users\user\Documents\Codex\2026-06-10\GGGsel\android-owner-app` and the
package is `com.clubtimer.owner`. Preserve application data. If no authorized
phone is connected, more than one phone is available, or build/install/version
verification fails, do not guess a target or use destructive recovery; report the
exact condition. This rule does not authorize a public release, Firebase writes,
uninstalling the app or clearing device data.
