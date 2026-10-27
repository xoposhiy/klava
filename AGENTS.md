# Klava

Windows tray app with one low-level keyboard hook: key chatter filter, AutoHotkey-style
hotkeys, and a Shift layout switcher (like Caramba Switcher or Punto Switcher).
`docs/spec.md` is the source of truth for behavior: read the section for the feature before
you change it, and update the spec in the same change. Add user-visible changes to
`CHANGELOG.md`.

## Commands

- Build: `dotnet build`. Test: `dotnet test`.
- Publish `dist\Klava.exe`: `powershell -File scripts/publish.ps1` (add `-Zip` for a release zip).
  Add `-Restart` to stop the running Klava first and start the new `dist\Klava.exe` after.
- Run: `dotnet run --project src/Klava.App`. Only one instance runs per session (mutex):
  stop a running Klava first, or the new one exits at once.

## Architecture rules

- Decision logic lives in `src/Klava.Core` and stays pure: no P/Invoke, no clock, no threads.
  The OS is behind `IPlatform`.
- A Core behavior change comes with a test that drives `Keyboard` from `tests/Klava.Core.Tests/TestKit.cs`
  with a timed key sequence. For a bug seen on a real keyboard, copy the timings from a
  `tools/kbdlog.py` log or from `Trace` lines in the Klava log into the test.
- `src/Klava.App` is thin Win32 glue. Hook callbacks run on a dedicated thread and must return
  within a few milliseconds: no file I/O, no UI calls, no waiting. A slow callback delays all
  typing on the machine, and Windows silently removes the hook. Log through `Log`, which queues.
- `tests/Klava.App.Tests` call the real Windows API. They return early when a needed layout
  is not installed. `InputReplayTests` send real input to the foreground window and run only
  with `KLAVA_INPUT_TESTS=1`; run them after changes to hotkey send sequences.
- Klava passes all injected events through and marks its own with `WindowsPlatform.Marker`.
- Log events, never typed text. The only exception is the opt-in `debug.traceKeys` trace
  (see "Tray app" in the spec); keep its default list empty.

## Done means

- `dotnet test` is green.
- After every bug fix or feature, run `powershell -File scripts/publish.ps1 -Restart`. The user
  runs `dist\Klava.exe` (also from autostart) and must get the new version at once. Report
  the restart, or the error if it failed.
- For a change in `src/Klava.App` or in hook-visible behavior: the related checks in
  `docs/manual-tests.md` pass. Agents cannot type on a real keyboard, so list the checks
  the user must run instead of claiming they pass.

## Gotchas

- Test with both an English and an AltGr layout. On ru-RU (Windows 10+), Right Alt is AltGr:
  Windows adds a fake Left Ctrl, and bugs that hide on en-US show up there.
- Windows reports a modifier that Klava released for a hotkey as up while the user still
  holds it. Code that asks the OS key state must account for that (see `Engine.Prune`).
- Caramba Switcher, Punto Switcher and AutoHotkey conflict with Klava; they must not run
  at the same time.
- Write code, comments, docs and commits in clear, simple English.
