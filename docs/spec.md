# Klava: specification

Klava is a Windows tray app that fixes and extends keyboard input. It replaces three tools
with one keyboard hook:

1. **Debounce filter**: drops key chatter (phantom repeated presses from a worn switch).
2. **Hotkeys**: the rules from the old AutoHotkey script (Unicode symbols on Right Alt,
   Home/End on Win+Ctrl+arrows).
3. **Layout switcher**: one Shift tap switches the layout, two Shift taps retype the last
   word in the other layout (the Caramba Switcher features).

One hook gives one fixed order of processing. Separate tools could not guarantee that the
debounce filter runs before the layout switcher sees the events.

## Background

Mechanical keyboards with worn switches or loose hot-swap sockets often chatter: one physical
press reaches Windows as two. The keyboard firmware should filter this, but often does not.
A Shift-based layout switcher makes chatter much worse: a phantom Shift press turns a single
tap into a double tap.

Measurements from one affected keyboard, recorded with `tools/kbdlog.py`. The same chatter
showed over USB and Bluetooth with stock Microsoft drivers, so the cause was the keyboard:

- Left Shift: a phantom press 1.5–4 ms (once 10 ms, once 21 ms) after the release, in about
  40% of single taps.
- M: a phantom release and press 8–13 ms apart in the middle of a normal press.
- The fastest real re-press of one key was 45 ms (arrow keys), and 64 ms for letters.

## Processing order

For each physical key event, the hook runs these stages in order. A stage can block the event;
blocked events do not reach later stages or other apps.

1. Skip injected events (from Klava or other apps): pass them through untouched.
2. Debounce filter.
3. Key state update (which physical keys are down).
4. Hotkeys.
5. Layout switcher (observes only, never blocks).

Mouse button and wheel events cancel a pending Shift tap and clear the word buffer.

## Debounce filter

- A key **press** that comes less than `thresholdMs` after the **release** of the same key is
  a bounce. Block that press, its autorepeats, and its release.
- The release of a blocked press also counts as a release of the key. One press can bounce
  several times, and the next press is measured from the last release, blocked or not.
- The key identity is the virtual key code plus the extended-key flag.
- `thresholdMs` (default 30) applies to all keys. `chatteringKeys` gives keys that chatter
  slower their own threshold, for example `{ "M": 50 }`. Keep thresholds below about 60 ms:
  fast typists repeat a letter 60–100 ms apart.
- The tray menu shows the keys with the fastest passed re-presses. A key that shows up there
  with gaps below about 45 ms is a candidate for `chatteringKeys`.
- Klava counts blocked bounces per key and shows them in the tray menu and in the log.
- Klava also logs each passed re-press of a printable key or a modifier that comes less than 100 ms after
  the release (`Fast re-press passed`), with the gap and the length of the previous press.
  Use these lines to tune thresholds when a doubled letter gets through.
- Time source: a high-resolution clock read when the hook callback starts. The hook runs on a
  dedicated thread with no UI work, so the callback starts within about 1 ms of the event.

## Hotkeys

- A rule has one or more triggers in AutoHotkey syntax and one action.
- Trigger syntax: modifier prefixes `#` (Win), `^` (Ctrl), `!` (Alt), `+` (Shift); `<` or `>`
  before a prefix requires the left or right key; then a key name (`x`, `,`, `Space`,
  `Right`, `F5`, `vkE8`). `<^>!` means AltGr (Left Ctrl + Right Alt).
- Modifier match is exact, like AutoHotkey without `*`: every required modifier is down, and
  no other modifier group is down. A side-specific requirement ignores the other side.
- Action: `text` (Unicode text) or `send` (AutoHotkey-like keys: `^{End}`, `{U+2715}`, `abc`).
- When a rule fires:
  1. Block the trigger key press and its release.
  2. Send the menu mask key (`vkE8`) if Alt or Win is down, so the release does not open the
     menu bar or the Start menu.
  3. Release the held modifiers for Windows, then send the action.
  4. Mark those modifiers as released. While the user holds a released modifier, block its
     autorepeat (it would press the key again for Windows). Pass its physical release, although
     Windows has the key up already: other apps' keyboard hooks can skip injected input, so they
     see only the physical release. A new press or the release of the key clears the mark.
- Never replace a physical modifier event with an injected one. ChatGPT's hotkey helper skips
  injected input. When Klava blocked the physical Right Alt release, the helper saw Right Alt
  down forever, and each later Left Alt press opened AppShots (hotkey "Alt + Alt", both Alt
  keys at the same time).
- Klava never presses modifiers again after an action. In v0.1 it did, and on AltGr layouts
  the fake Left Ctrl stayed down: Windows creates the fake Ctrl release only from a real
  Right Alt release, and Klava blocked that release.
- AltGr: on some layouts (ru-RU on Windows 10 and later), Windows adds a fake Left Ctrl press
  (scan code 0x21D) before Right Alt. Klava treats it as Left Ctrl for matching, so `<^>!x`
  matches, and drops it when Right Alt goes up.
- While a released modifier is held, other keys reach apps without that modifier, and further
  hotkeys still match.
- Missed releases: Windows reports a released modifier as up even while the user holds it, so
  the OS key state cannot show a missed release (for example, the release went to an elevated
  window or the lock screen). A held key autorepeats at least every second, so Klava drops a
  released modifier when it had no event for 1.5 s. Without this, a missed Right Alt release
  made every later `x` type `✕`.
- Autorepeat of the trigger key fires the rule again.
- Klava logs each send for a hotkey with its steps.
- Safety net for stuck modifiers: 150 ms after the physical release of any modifier, Klava
  checks all modifiers. For each one that Klava sees released but Windows reports down, Klava
  sends the mask key and the release, and logs a warning. The warning in the log is a bug to
  investigate, not normal behavior.
- Synthetic navigation keys (arrows, Home, End, PgUp, PgDn, Insert, Delete) and right-side
  modifiers carry the extended-key flag. Without it, apps see numpad keys, and Windows treats
  Right Alt and Right Ctrl as left keys.
- The default rules are a port of the old `autohotkey.ahk` script. The port fixes two bugs:
  AltGr+Shift+2 and AltGr+Shift+3 sent control characters U+0082 and U+0083 instead of
  ₂ and ₃.

## Layout switcher

### Shift tap

A **tap** is a press and release of one Shift key where:

- no other key is down when Shift goes down,
- no other key goes down and no mouse button is clicked before Shift goes up,
- Shift is held at most `maxTapMs` (default 500).

### Single tap

Switch the foreground window to the next layout in `layouts` (default `en-US`, `ru-RU`).
Switch on the release, without waiting for a possible second tap.

### Double tap

A second tap that starts at most `doubleTapMs` (default 400) after the first tap ended
converts the last word. When the word buffer has no last word, the second tap converts the
selected text instead (see "Selected text").

To convert the last word:

1. The first tap already switched the layout. The second tap does not switch again.
2. Delete the last word with Backspace and type it again as the characters that the same
   physical keys give in the new layout. Klava computes the characters itself and sends them
   as Unicode, so the result does not depend on when the app applies the layout change.
3. Trailing spaces after the word are deleted and typed again too.

A third tap starts a new sequence (it is a single tap).

### Word buffer

The buffer holds the physical keys typed since the last reset: virtual key, scan code,
Shift state, Caps Lock state.

- Printable keys (letters, digits, OEM punctuation, numpad digits, Space) with no Ctrl, Alt,
  or Win down are added.
- Backspace with no Ctrl, Alt, or Win removes the last key.
- Shift and Caps Lock do not change the buffer.
- Any other key, any key with Ctrl, Alt, or Win, a mouse click, a change of the foreground
  window, and a fired hotkey clear the buffer.
- After a single tap, the next printable key clears the buffer first, so the last word never
  mixes text typed before and after a layout switch.
- The last word is the keys after the last Space, not counting trailing Spaces. The buffer
  keeps at most 100 keys.

### Selected text

Background: `docs/research/selection-conversion.md` describes how other switchers do this,
with links to their source code. All Windows switchers there use the clipboard; Klava does
the same.

When it runs: on a double tap when the word buffer has no last word. This is the case when
the last keys before the taps were not text (arrows, Shift+arrows, Ctrl+A, Enter, and so on),
or when a mouse click came after the text. If the user typed text after a selection, the
typed text replaced the selection, and the buffer has a last word again.

The layouts: the first tap already switched the layout. Klava converts the text from the
layout before the first tap to the layout after it, and does not switch the layout again.

Steps:

1. Check the foreground window. Skip console windows: the classes `ConsoleWindowClass`,
   `CASCADIA_HOSTING_WINDOW_CLASS`, `mintty`, `VirtualConsoleClass`. In a console, Ctrl+C
   stops the running program.
2. Wait up to 100 ms until no modifier is down; if one is still down, skip. Ctrl+C with a held
   Shift is Ctrl+Shift+C. The wait is needed because the hook sees the Shift release before
   Windows updates the key state.
3. Save the clipboard: the standard formats (text, locale, HTML, RTF, DIB, PNG, file list)
   in their original order, at most 32 MB in total. Do not read private formats: reading
   some of them (Excel `ShadowWorkbook`) breaks the paste in the source app. Record the
   clipboard sequence number (`GetClipboardSequenceNumber`).
4. Send Ctrl+C. Do not empty the clipboard first.
5. Check the sequence number every 10 ms for up to 400 ms. If the number does not change,
   nothing is selected: stop.
6. Read the copied text (`CF_UNICODETEXT`). Restore the clipboard and stop if one of these
   is true:
   - the text is empty;
   - the text is longer than `maxSelectionChars` (default 20);
   - the text has a line break (CR or LF);
   - the clipboard has a format that marks a whole-line copy of an empty selection:
     `MSDEVLineSelect` (Scintilla, Notepad++), `VisualStudioEditorOperationsLineCutCopyClipboardTag`
     (Visual Studio), or VS Code editor data with `"isFromEmptySelection": true`. Look for the
     VS Code data in `vscode-editor-data` and in `Chromium Web Custom MIME Data Format`, where
     Chromium puts custom web formats on Windows. Check the data as UTF-16 and as UTF-8 text.
   Some editors (Notepad++, Visual Studio, VS Code, JetBrains IDEs) copy the whole line on
   Ctrl+C with an empty selection. The length limit and the line break check stop most of
   these copies; the marker formats stop the rest where the editor sets them.
7. Convert the text character by character. For each character, find the key and the Shift
   state that type it in the source layout, then type that key in the target layout, as in
   the last word conversion. Keep the character unchanged when the source layout has no key
   for it, when the key needs Ctrl or AltGr, or when the target layout gives no character
   (dead keys). If the result is equal to the copied text, restore the clipboard and stop.
8. Put the result on the clipboard as `CF_UNICODETEXT` with these extra formats, so the
   temporary text does not go into the Win+V history or the cloud clipboard:
   - `ExcludeClipboardContentFromMonitorProcessing` (any data),
   - `CanIncludeInClipboardHistory` = DWORD 0,
   - `CanUploadToCloudClipboard` = DWORD 0.
   Record the new sequence number.
9. Send Ctrl+V. The pasted text replaces the selection. The selection is not restored after
   the paste.
10. Wait 250 ms, then restore the saved clipboard with the same three extra formats. Restore
    only if the sequence number is still the one from step 8: a newer number means that the
    user or an app put new data on the clipboard, and Klava keeps that data.

Rules for all steps:

- The hook callback only starts the conversion. Steps 1–10 run on a separate worker thread,
  because they wait for other apps. Klava does not block the user's keys during the
  conversion.
- The worker thread owns a message-only window, the clipboard owner. Other apps send messages
  to the clipboard owner and wait for the answer, so the worker handles sent messages in all
  its waits.
- A double tap while a conversion runs does nothing.
- Open the clipboard with retries: up to 10 tries, 10 ms apart. If Klava cannot open the
  clipboard, log a warning and stop; restore the clipboard if Klava already changed it.
- Ctrl+C and Ctrl+V go out as Klava's own marked input (`WindowsPlatform.Marker`), so the
  hook passes them through and the word buffer does not see them.
- After the conversion, the word buffer stays empty.
- `maxSelectionChars` = 0 turns off the selected text conversion. The last word conversion
  still works.
- The log gets one `Selected text:` line for each double tap that goes to this path: the
  result (converted, nothing selected, skipped and why, error) and the number of characters.
  Errors are warnings. Never log the text.

Architecture:

- `IPlatform.SwitchToNextLayout` returns the layouts before and after the switch.
- `LayoutSwitcher` (Core) decides when to convert the selection and calls
  `IPlatform.ConvertSelection` with the source and the target layouts. The call returns at once.
- `SelectionText` (Core) holds the checks of step 6 and the character conversion of step 7,
  with tests. The conversion uses `IPlatform.FindKey`: the key and the Shift state that type
  a character in a layout (`VkKeyScanEx`).
- `SelectionConverter` (App) holds the worker thread, the clipboard work, and the window class
  check.

Known limits:

- The app's own Ctrl+C result (step 4) goes into the Win+V history; Klava cannot mark it.
- A clipboard manager can read or change the clipboard between the steps.
- In apps that ignore Ctrl+C or Ctrl+V from `SendInput`, nothing happens.
- A short line (at most `maxSelectionChars`) that an editor copies on an empty selection
  without a marker format and without a line break is converted and pasted at the caret.
  Undo (Ctrl+Z) reverts it.
- The restore keeps only the standard formats of step 3. Other clipboard data from before the
  conversion is lost: for example, Excel cells paste as text or HTML, and files cut in
  Explorer paste as a copy.
- Text in the target layout does not change: a selected `привет` with the EN layout active
  stays as is. Tap Shift twice again: the conversion then goes from RU to EN.
- Klava reads delayed-rendered formats, so the app that owns the clipboard renders them.
  A hung clipboard owner can block the worker thread and later conversions. Typing is not
  blocked.

### Switch method

- `Message` (default): post `WM_INPUTLANGCHANGEREQUEST` with the target layout to the focused
  window of the foreground thread.
- `AltShift`, `CtrlShift`, `WinSpace`: send the Windows hotkey. Use it for apps that ignore
  the message.

## Configuration

- File: `%APPDATA%\Klava\config.json`. Klava creates it from the embedded default on first run.
- `switcher.maxSelectionChars`: 0..1000, default 20 (see "Selected text").
- JSON with comments and trailing commas allowed. See `src/Klava.Core/default-config.json`.
- The tray menu reloads the file. An invalid file keeps the previous configuration and shows
  the error.

## Tray app

- Tray icon menu: Enabled, Debounce, Hotkeys, Layout switcher (check boxes), blocked bounce
  counts, Open config, Reload config, Open log, Start with Windows, Exit.
- One instance per user session.
- On start, warn if Caramba Switcher, Punto Switcher, or AutoHotkey runs: they conflict with Klava.
- Log file: `%LOCALAPPDATA%\Klava\klava.log`. Log events (start, config errors, blocked bounces,
  layout switches, conversions), never typed text.
- Exception, opt-in for debugging: `debug.traceKeys` lists keys to trace. Each press of a traced
  key gives one `Trace` line: the previous key and the pause since its press, the press length
  and autorepeats, the next key and the pause after the release (or "during the hold"), the
  time since the last release of the same key, and which presses the filter blocked. The tracer
  sees events before the debounce filter. The neighbor keys are fragments of typed text, so
  the default list is empty.

## Architecture

- `src/Klava.Core`: all decision logic. Pure C#, no Win32 calls. The platform is behind the
  `IPlatform` interface, so tests drive the logic with recorded key sequences.
- `src/Klava.App`: Win32 glue: low-level keyboard and mouse hooks on a dedicated thread,
  `SendInput`, layout API, tray UI, config file, autostart.
- `tests/Klava.Core.Tests`: xUnit tests for the core.
- `tests/Klava.App.Tests`: integration tests against the real Windows layout and input API.
- `tools/kbdlog.py`: diagnostic logger (Raw Input + hook). Use it to check chatter and
  switcher behavior on the real keyboard.

## Known limits and risks

- Windows removes a low-level hook if the callback is too slow (`LowLevelHooksTimeout`).
  Keep callbacks fast; heavy work in the callback breaks all typing.
- A non-elevated Klava does not see input for elevated windows.
- Some apps may ignore `WM_INPUTLANGCHANGEREQUEST`; use a hotkey switch method for them.
- Raw Input clients (some games) still see the bounces: the filter works at the hook level.

## Not in scope yet

- Automatic layout detection while typing.
- Per-app rules.
