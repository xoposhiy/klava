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

### Chatter history

- Save daily counts and recent minute counts for each virtual key in `%LOCALAPPDATA%\Klava\chatter-history.json`.
  Keep the history across restarts and config reloads, with no automatic expiry.
- Count each blocked bounce once, not its release or autorepeats. Count passed fast re-presses
  separately; these events can be intentional and are not confirmed chatter.
- Group release-to-press gaps into 10 ms buckets from 0 to 199 ms. Passed fast re-presses
  cover only 0 to 99 ms. The label `0-9` means a 0 to 9 ms release-to-press gap.
  Do not save typed text, key order, or individual event times.
- Count all non-injected key presses, including blocked bounces, hotkey triggers, and presses
  while paused. Exclude releases and repeated key-down events during a hold. Count each
  virtual key plus extended flag separately, then combine by virtual key for display.
- Save recent counts in local minute buckets. Merge buckets older than two hours into daily
  counts. `Last hour` includes the current minute and the previous 60 minutes, so the boundary
  has up to one minute of extra coverage. Refresh the rolling window even with no new input.
- Store only counts with known press totals in format version 3. Do not import old history or
  log entries. Each displayed count and rate uses the same recorded presses.
- The hook only queues events. A separate worker updates counts and saves at most once every
  five seconds. Write a temporary file, flush it, then replace the history file. A normal Exit
  drains pending events and saves them. A forced stop can lose the last few seconds.
- Apply queued events in batches. Merge old minute buckets once per minute. Build display
  snapshots only when a reader requests them, at most once per second. Save the worker's live
  records without copying all gap arrays. With the history window closed, typing does not
  rebuild the full history for display.
- If the history file cannot be read or has an unsupported format, keep the file unchanged
  and collect new counts only in memory. If saving fails, keep counts in memory and retry.
  Show the error state in the history window and log the error.
- Tray menu: `Chatter history...` opens one window; opening it again brings it to the front.
  Closing the window keeps Klava running and collecting history.
- Scale window sizes and chart spacing with the monitor DPI. Size text rows to fit their
  content, wrap long notes, and keep chart labels apart when the window is resized.
- Show a table of keys, blocked bounces, and passed fast re-presses for the selected period:
  the last hour, today, the last 7, 30, or 90 calendar days including today, or all time.
  Include an `All keys` row and show total presses and blocked and fast-passed percentages.
  Each percentage uses the displayed event count divided by the displayed press total for
  the same key and period. Show `N/A` when the press total is zero. Show a positive rate below
  0.01% as `<0.01%`. Do not round a positive event count to 0%.
  Explain that blocked repeats were stopped and passed fast repeats were allowed and may be
  intentional. Do not show incomplete totals, old event counts, or compatibility controls.
- Selecting a row shows its counts in a chart. `Daily history` shows counts over time,
  including zero-event days. For more than 60 days, combine days into at most 60 bars.
  `Re-press gaps` shows counts for each 10 ms gap bucket. Use separate colors for blocked
  bounces and passed fast re-presses, with exact counts and rates on hover.
  For the last hour, show minute bars.
  Refresh once per second. Requested counts appear on the next refresh, normally within
  about two seconds.
- History records the filter's decisions at the time of each event. Changing a threshold
  does not change old counts. The session counts in the tray keep their current meaning.

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

Read the Caps Lock toggle state from Windows when recording each key. Do not infer it by
counting physical Caps Lock presses: other apps can block a press or reset Caps Lock with
injected input, and Caps Lock can change while Klava is paused. Retyping keeps the state
recorded for each key, even if Caps Lock changes before the double tap.

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

## Selection translation

- `translation.enabled` defaults to true. `translation.hotkey` defaults to `^<!t`:
  Ctrl+Left Alt+T. The left Alt requirement prevents AltGr from triggering translation.
  Use the existing hook and hotkey modifier release sequence. Block the trigger press,
  all hold repeats (even after modifiers are released), and the trigger release. Pass physical
  modifier releases through. One hold starts one capture. Clear the layout switcher word buffer.
- The global Enabled and Hotkeys toggles disable the translation hotkey. Translation rules
  take priority over user send rules with the same trigger. Require a non-modifier trigger
  with Ctrl, Alt, or Win. Reject an invalid trigger, null keys, empty model, null section, or
  `maxChars` outside 1..5000 as config errors. Existing files without this section get defaults.
- Capture selected text on the existing selection worker. Translation capture and layout
  conversion share its busy guard and clipboard window. Never wait or do network, file, or
  UI work in the hook callback. Skip terminal window classes. Wait for modifiers to be up,
  save the standard clipboard formats, send marked Ctrl+C, and wait up to 400 ms for a change.
  Do not empty the clipboard first. Check that the foreground window still matches before
  copying and after the copy. Ignore whole-line copy markers using the layout conversion checks.
- Allow multiline text. Reject empty or whitespace-only text and selections longer than
  `translation.maxChars` (default 5000), counted as Unicode scalar values. Trim outer whitespace.
  Restore saved clipboard formats before opening the result window. Restore only while the
  clipboard sequence still matches the copy. Preserve newer clipboard data. The clipboard
  format and history limits from Selected text also apply. If capture fails, show the reason
  in the result window and send no translation request. Busy requests show a short error.
- Open one resizable result window near the pointer, inside the monitor's working area.
  Keep the result window above other windows. Use a simple light window with a standard
  Windows title bar and resize border, native language menus, and standard text buttons.
  Use muted source text and a larger translation. Let Windows move and resize the window.
  Do not draw custom button text, icons, rounded frames, or non-client window areas.
  Start at 540 × 410 logical pixels; grow for long translations or AI details within the
  monitor height. Keep controls and text usable at 100%, 150%, and 200% display scaling.
- Show source and target language menus with Auto, EN, RU, and DE choices. Display full
  detected language names when Auto resolves. Provide Retry (DeepL), AI details, Copy,
  Settings, and the system close button. Keep tooltips, accessible names, and visible keyboard
  focus. Size buttons to their captions and keep footer actions on the same row.
  Finish building the controls before applying DPI scaling; do not mix unscaled controls
  with scaled fonts. Show a plain loading message. Keep a quick translation
  readable and copyable while AI details load. Show AI word meanings with separate text
  sizes and colors for headwords, parts of speech, translations, and examples.
- Render plain text in native controls. Support selection, mouse-wheel and keyboard scrolling,
  and the standard scroll bar. Esc closes the window. Ctrl+Enter requests AI details.
  Ctrl+C copies the result unless the focused text control has selected text, in which case
  it copies that selection. The Copy button briefly shows Copied after success. Opening
  settings opens the config and closes the popup. Closing never exits Klava.
- Show short setup hints in the card or below the existing translation. Keep the field name
  and config path in a tooltip. Missing AI setup keeps the quick translation and Copy available.
- The hotkey starts DeepL only. Use `POST /v2/translate`, JSON `text` array, optional
  `source_lang`, and `target_lang`, with `Authorization: DeepL-Auth-Key <key>`. Keys ending
  in `:fx` use api-free.deepl.com; other keys use api.deepl.com. Default Auto directions:
  EN → RU, RU → EN, DE → RU. With an unknown source, first request RU; if DeepL detects RU,
  request EN. An explicit source avoids the extra request. Support all six EN/RU/DE directions.
  Reject other detected source languages and ask the user to choose the source. Changing a
  language starts another DeepL request. Explicit equal source and target produce a hint.
- AI details alone calls OpenAI Responses with a strict JSON schema and `store: false`.
  Use `translation.model` (default `gpt-6-luna`) and explicitly send `reasoning.effort: "none"`.
  Use the last DeepL source and target when
  the selectors are Auto; otherwise use the explicit choices. Without a DeepL result, AI
  detects the source and uses the same default directions. Treat selected text as data in a
  separate user message. No tools or app context are sent.
- One word (letters and combining marks, including internal apostrophes and hyphens; ignore
  outer whitespace and common enclosing punctuation) requests a dictionary-style AI answer:
  headword, common distinct senses, parts of speech, translation equivalents, and source and
  translated examples. For German nouns include article and plural. A phrase requests the
  main translation and up to three distinct translations when missing context changes meaning.
  Label AI output as generated and possibly incomplete. Do not claim a complete dictionary.
- Keys: `translation.deeplKey` and `translation.openaiKey`, default empty. Empty keys use
  `DEEPL_API_KEY` and `OPENAI_API_KEY` from the process environment. Missing keys show the
  field name and config path in the error tooltip. AI setup is optional and cannot prevent DeepL translation.
  Reload config closes the window, cancels pending requests, and clears the caches.
- Cache up to 100 successful results per provider by text, source, and target in memory.
  A key or model change clears that provider's cache. Do not cache errors or cancelled replies.
  Never save or log text, translations, keys, response bodies, or service exception messages.
  Logs contain provider, character count, and status only. Source text leaves the machine
  only in response to the hotkey, a DeepL language selection/retry, or the AI button.
- Bound response bodies to 1 MiB. Use a 20-second HTTP timeout and an overall 25-second
  DeepL or 40-second AI deadline. A newer request or window close cancels the old request.
  Ignore late results even if the service ignores cancellation. Show distinct key, quota,
  rate limit, network, timeout, refusal, and malformed response errors in the window. Keep
  the buttons available for retry and language correction. Copy result is an explicit
  clipboard change; translation never replaces the text in the original app.

## Configuration

- File: `%APPDATA%\Klava\config.json`. Klava creates it from the embedded default on first run.
- `switcher.maxSelectionChars`: 0..1000, default 20 (see "Selected text").
- JSON with comments and trailing commas allowed. See `src/Klava.Core/default-config.json`.
- The tray menu reloads the file. An invalid file keeps the previous configuration and shows
  the error. On start, an invalid file shows the error and uses the default configuration.
  Reject null sections, null lists and list items, empty key names or language tags, unsupported
  switch methods, and invalid Unicode code points in send actions as configuration errors.

## Tray app

- Tray icon menu: Enabled, Debounce, Hotkeys, Layout switcher (check boxes), blocked bounce
  counts, Chatter history, Open config, Reload config, Open log, Open error log, Start with Windows, Exit.
- One instance per user session.
- Copy session counters under a short lock. Sort and format the counters and update menu
  controls after releasing the lock, so UI work does not hold up hook callbacks.
- On start, warn if Caramba Switcher, Punto Switcher, or AutoHotkey runs: they conflict with Klava.
- Log file: `%LOCALAPPDATA%\Klava\klava.log`. Log events (start, config errors, blocked bounces,
  layout switches, conversions), never typed text.
- Error log: `%LOCALAPPDATA%\Klava\errors.log`. Include the time with UTC offset, process ID,
  version, and full exception. Write fatal errors directly and flush them to disk without
  waiting for the event log worker. Hook callbacks still only queue handled errors.
  If the error log is locked, write a separate `error-*.log` in the same folder. If that
  folder is unavailable, try `%TEMP%\Klava`. Keep error reports across event log rotation.
  `KLAVA_LOG_DIR` can select an isolated log and session folder for diagnostic test processes.
- Track the running session in `running-session.txt`. Remove it after a clean exit. At the
  next start, report a remaining marker as an unclean exit with an unknown cause; a forced
  stop or system shutdown is not proof of a crash. Forced stops cannot write an exception.
- Log flush waits for prior writes to finish, with a two-second timeout. A failed file write
  must not terminate the log worker or keyboard input.
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
