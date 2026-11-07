# Manual tests

Unit tests cannot prove how the hooks behave in real apps. Run these checks after any change in
`src/Klava.App`, and the checks for the feature you changed after a change in `src/Klava.Core`.

## Setup

1. Close Caramba Switcher and AutoHotkey.
2. Run `dotnet run --project src/Klava.App`, or start `dist\Klava.exe`.
3. Check that the tray shows the blue "K" icon and that `%LOCALAPPDATA%\Klava\klava.log` has
   a `Started` line. Startup must not open or briefly show any Klava windows.

Automated UI checks in `dotnet test` and `scripts/publish.ps1` render transparent windows
without taskbar entries. `dotnet run --project tests/Klava.UiProbe` opens a visible preview;
`--verify` keeps that preview transparent. Tests with `KLAVA_INPUT_TESTS=1` use real input
and can open a foreground test window.

## Checks

Do each check in Notepad, in Chrome, and in Windows Terminal.

| # | Action | Expected result |
|---|--------|-----------------|
| 1 | Tap Left Shift once | The layout indicator changes. Log: `Layout switched`. |
| 2 | Tap Right Shift once | Same as check 1. |
| 3 | Type `Hello` with Shift held for `H` | Capital H; the layout does not change. |
| 4 | Shift+click in text | Text is selected; the layout does not change. |
| 5 | In EN, type `ghbdtn`, tap Shift twice | The text becomes `привет`; the layout is RU. |
| 6 | Type `ghbdtn ` (with a space), tap Shift twice | `привет ` with the space kept. |
| 6a | In EN with Caps Lock off, type `ghbdtn`, tap Shift twice; repeat after using the dictation hotkey | Both results are lowercase `привет`; later typing keeps its normal case. Run in both EN and RU. |
| 6b | Pause Klava, turn Caps Lock on, resume; in EN type `ghbdtn`, tap Shift twice | The text becomes `ПРИВЕТ`. Turn Caps Lock off afterward. |
| 6c | In EN with Caps Lock on, type `ghb`; turn Caps Lock off, type `dtn`, tap Shift twice | The text becomes `ПРИвет`. |
| 7 | Type a word, press Enter, tap Shift twice | Nothing is retyped; the layout switches once. |
| 7a | In EN, type `ghbdtn`, select it with Shift+Home, tap Shift twice | The text becomes `привет`; the layout is RU. |
| 7b | Select `ghbdtn` with a mouse double-click, tap Shift twice | Same as check 7a. |
| 7c | Copy `abc`, do check 7a, then paste with Ctrl+V | `abc` is pasted. Win+V history has no `привет`. |
| 7d | Click in a text line with no selection, tap Shift twice | Nothing changes in the text. Log: nothing selected. |
| 7e | In VS Code and Notepad++: place the caret on a short line with no selection, tap Shift twice | The line does not change. |
| 7f | Select more than 20 characters, tap Shift twice | The text does not change; the clipboard keeps its old content. |
| 7g | In Windows Terminal, select text, tap Shift twice | Nothing is copied or pasted; a running program is not stopped. |
| 7h | Copy an image (Win+Shift+S), do check 7a, then paste in Paint | The image is pasted. |
| 7i | Select `ghbdtn`, tap Shift twice, and at once tap Shift twice again | The text is `привет` or `ghbdtn`, never a mix; the clipboard keeps its old content. |
| 8 | Right Alt+X, Right Alt+Shift+comma | `✕`, then `≤`. The menu bar does not open on Alt release. |
| 8a | In the RU layout (Right Alt is AltGr there): Right Alt+-, release, type `привет` | `—привет`; no Ctrl or Alt stays down; no `WARN` in the log. |
| 8b | Hold Right Alt, press A, then B, release | `αβ` |
| 8c | With ChatGPT running and its AppShots hotkey set to Alt + Alt: in EN and in RU, do Right Alt+0, release, then tap Left Alt 3 times | `→`; AppShots does not open. |
| 9 | Win+Ctrl+Right in a text line | The caret goes to the end of the line; the virtual desktop stays. |
| 10 | Release Win after check 9 | The Start menu does not open. |
| 11 | On a keyboard that chatters, type normally for 10 minutes | The tray menu shows blocked bounces; no doubled letters. |
| 11a | Hold Right Alt, press X, then Win+L; unlock; type `x` | A plain `x`, not `✕`. |
| 11b | Keep Klava running during normal work in EN and RU, including special keyboard keys and lock/unlock; then open Chatter history | Klava stays in the tray, history keeps updating, and the log has no new unhandled exceptions. |
| 12 | Tray: uncheck Enabled | Icon turns gray; Shift taps and hotkeys do nothing. |
| 13 | Edit config with an error, Reload config | A warning balloon; the old config keeps working. |
| 13a | In a test config, try `"debounce": null`, a null hotkey rule, and `"send": "{U+110000}"`; reload each, then restart with one invalid config | Reload keeps working settings. Restart shows a warning and uses defaults. Klava stays running. Restore the valid config afterward. |
| 14 | Tray: open Chatter history | One window shows saved counts by key, with no imported log entries. |
| 14a | Select M, then All keys; switch periods and chart types; hover over bars | Counts match the selected key and period. Both chart types show blocked and passed counts, with exact counts on hover. |
| 14b | Keep the history window open while typing in EN and RU, including Right Alt hotkeys and Shift taps | Typing, hotkeys, and layout switching work normally. New bounce counts appear within about two seconds. |
| 14c | Close the history window, then open it twice from the tray | Klava keeps running; only one history window opens. |
| 14d | Note history counts, Exit from the tray, then start Klava again; reload config | Saved counts remain; old log entries are not counted again. |
| 14f | Select Today and Last hour; tap a key, then hold it; try EN and RU | Total presses increase once per tap or hold, plus any detected bounces. Hold repeats do not increase totals. The hourly chart shows minute bars. |
| 14g | Compare a key row and All keys | Each rate equals the displayed event count divided by Total presses. No `+`, unknown totals, or old-data controls appear. Charts use the same records as the table. |
| 14h | Leave Last hour open without typing for over a minute; restart Klava | The rolling window advances; saved recent totals and rates remain after restart. |
| 14j | With enough keys to scroll, select a key, scroll away from it, and wait for new counts. Repeat in Last hour. | The selected key, current column, and visible top key remain stable. Last hour does not redraw every second while idle. |
| 14i | With several months of saved history, close the history window and type normally; open the tray menu and history window, then Exit and restart | Typing stays responsive. The menu shows session counts. The history window catches up within about two seconds and counts remain after restart. |
| 14e | Resize the history window down to its minimum size; check at 100%, 150%, and 200% display scaling, and move it between monitors with different scaling | The title, filters, notes, table, legend, status, and chart labels remain readable without overlap or clipped text. Check both charts and Last hour. |

## Typing episode and dictionary checks

Run in Notepad, VS Code, and a browser input with EN and RU/AltGr layouts. Close other
keyboard tools first. Open **Typing episodes...** from the tray for manual analysis.
For E1–E3, explicitly add the test words `llm` and `ast` in the Dictionary tab first.

| # | Action | Expected result |
|---|--------|-----------------|
| E1 | In RU, type the physical keys for `llm` and pause; then press Space. Repeat with `ast`, Enter, and punctuation. | No change before the boundary; `ддь` becomes `llm`, `фые` becomes `ast`. Space/punctuation occurs once. Enter submits the corrected text. RU stays active. |
| E2 | Type the uppercase equivalents, a mixed-case word, `ддь1`, and a word outside the dictionary. | Case is preserved. Only whole dictionary matches change. Digits prevent a partial match. |
| E3 | Type a dictionary word in RU; click elsewhere, move the caret, select text, or use Ctrl/Alt/Win before the boundary. | No stale word is deleted or corrected at the new caret. Existing Right Alt hotkeys work and leave no held modifiers. |
| E4 | Type and fix words with Backspace/Delete, Shift-Shift, a temporary layout visit, and Triple-Shift → text → Shift. Select text by double click and drag, then type. Pause for 3 seconds. | Episodes include the surrounding characters, key times/layouts, mouse details, layout reasons, and correction semantics. Adjacent triggers share an episode. Separate fields/windows do not share text. |
| E5 | Type ordinary text without edits, then trigger episodes; click Analyze saved episodes twice. | Usage includes ordinary completed words. Error episodes and signal counts are separate. The second analysis does not double counts. No word is approved automatically. |
| E6 | Select an English candidate and approve it; hold a modifier while the update is applied; restart Klava. | The word works at the next boundary. Held-key state stays correct. The approval remains after restart. |
| E7 | Type unique test strings in native and browser password fields, then return to normal fields. Inspect the local episode JSON files and logs. | Password strings/key codes are absent when fields are recognized. Unknown/stale fields are skipped. Normal collection resumes. Typed text appears only in typing files, never ordinary/error logs. |
| E8 | Set typing.autocorrect=false; then typing.enabled=false, reloading each time. Pause Enabled as well. Restore settings. | The first setting keeps collection and stops replacements. The second and pause stop both, including selected-text correction recording. |
| E9 | Publish with `-Restart`. Before opening any other Klava window, select **Typing episodes...** once from the tray. Minimize the window and select the same item again. Resize to minimum at 100%, 150%, and 200% DPI. Keep typing during a large manual analysis. | The first selection shows the window in front. The second restores the same window. It remains readable. Analysis does not delay typing. Closing the window leaves collection active. |
| E10 | In an isolated test copy, age raw files to day 6 and day 7; restart with expired unanalyzed files. | A balloon offers analysis before deletion. Raw files expire; results, usage, approvals, and heuristic-version runs remain. Expiry does not start analysis. |
| E11 | In RU, switch to EN, type `gjnjv`, then use Shift-Shift to get `потом`. Run analysis. Repeat with a clean EN visit for `http`. | `gjnjv` is not proposed. The clean `http` visit supplies evidence. Unapproved words appear in Candidates; approved words appear in Dictionary. |
| E12 | After a correction, type another word and edit its last letter; then try Shift+Home followed by Delete. In a separate correction, use immediate Ctrl+Z or Backspace into the corrected word. | The unrelated edit and navigation do not produce a false-positive signal. Undo or Backspace reaching the corrected word can produce one. |
| E13 | Start an isolated copy with no approved words. Open Dictionary, type RU equivalents of the specification's example words, then add one word manually and restart. | Dictionary starts empty and no example word is corrected. Only the added word appears and becomes active. It remains visible without any analysis or usage. No word list appears in config. |
| E14 | With `cd` unapproved, start an empty line in RU. Type the keys for `cd` and Space, paste a single-line argument, press Home and Delete twice, switch to EN, type `cd` and press Enter. Click **Analyze saved episodes**. | The Candidates row shows `cd` beside `св` with a `line-start-retype` signal. A second analysis keeps the same counts. |
| E15 | Sort Candidates by each column in both directions. Select a row, scroll away from it, and wait at least 10 seconds; type in another app to update usage. | Sort direction, selected word, current column, and visible top word stay stable. Counts sort as numbers. The table does not reset on unchanged data. |
| E16 | Scroll Dictionary, select a word, and leave a partly typed word in Add word. Wait at least 10 seconds. Enter invalid input and click Add word, then wait again. Select text in repeated patterns and wait. | Dictionary selection and scroll position, draft input, and unchanged pattern selection remain stable. The validation message stays until input changes. Approve selected word is disabled on Dictionary or without a selected candidate. |
| E17 | Start analysis with enough saved episodes to keep it running for several seconds. | Analyze saved episodes stays disabled until analysis finishes. Collection continues and the result appears without changing the candidate sort. |

For offline analysis, exit Klava and run `dotnet run --project tests/Klava.UiProbe --
--analyze-typing <typing-folder>`. This uses the normal store and heuristic versions, keeps
usage counts and approvals, and prints a JSON snapshot. Do not run it while Klava is active.

## Selection translation checks

Configure `translation.deeplKey` and optionally `translation.openaiKey`, then Reload config.
Run the app checks in Notepad, Chrome, and VS Code with EN and an AltGr layout (RU or DE).

| # | Action | Expected result |
|---|--------|-----------------|
| T1 | Select an English phrase; press Ctrl+Left Alt+T | A DeepL RU translation opens near the pointer. Original text stays selected and unchanged. No AI request is made. |
| T2 | Select a Russian phrase, then a German phrase; use the hotkey for each | RU → EN and DE → RU. Check all six directions using the language menus. |
| T3 | Select `bank`, then click AI details | DeepL gives the initial result. Only the button requests AI senses, parts of speech, and examples. The result is labeled AI-generated. |
| T4 | Select an ambiguous phrase and click AI details | Main translation, with different interpretations and context notes when needed. For `Gift`, compare explicit EN and DE sources. |
| T5 | Copy text or an image, translate a selection, then paste the clipboard elsewhere | The prior clipboard content is preserved. Copy result replaces it only when clicked. |
| T6 | Hold the translation trigger, release modifiers before T; repeat with an AltGr layout | One request per hold; no typed T or stuck modifiers. Right Alt+T does not open translation. Existing Right Alt symbol hotkeys still work. |
| T7 | Try no selection in Chrome and VS Code; try multiline text and over 5000 characters | No-selection hints, multiline translation, and length-limit hint. VS Code whole-line copies are skipped. |
| T8 | Use the hotkey in Windows Terminal; change foreground app immediately after the hotkey | No Ctrl+C is sent to the terminal. A changed foreground app produces a hint rather than a translation of another app's text. |
| T9 | Close with Esc while loading; request another translation; switch languages quickly; reload config | No late result reopens the window or replaces the newer result. Typing stays responsive. |
| T10 | Test missing and invalid keys, no network, quota/rate errors if available | A readable error in the window; retry buttons remain usable. Missing OpenAI setup does not prevent DeepL. Logs have no selected text, translations, or credentials. |
| T11 | Pause Enabled or Hotkeys; set translation.enabled to false and reload | Translation hotkey is inactive. Restore settings afterward. |
| T12 | Resize the window at 100%, 150%, and 200% scaling and open near monitor edges | Text, status, selectors, and buttons stay readable; long results scroll; the window opens inside the screen. |
| T13 | Drag the standard title bar; resize each edge and corner; use Tab and Shift+Tab; hide and show the window | No duplicate frames or paint artifacts. Captions remain complete and footer buttons remain aligned. Keyboard focus is visible. Esc closes it. |
| T14 | Open language menus and choose EN, RU, DE, and Auto; click Retry | Standard menus show complete names. Language names reflect detection or the chosen language. Retry uses DeepL. |
| T15 | Press Ctrl+C with no text selected, then with part of the translation selected; use the Copy button | Full result, selected part, and full result are copied respectively. The Copy button briefly shows Copied. |
| T16 | Open a long translation or AI article; scroll with the wheel, keyboard, and scroll bar | All content is reachable. Word senses, translations, and examples have clear visual separation. |
| T17 | Press Ctrl+Enter; retry with missing OpenAI setup; click Settings | Ctrl+Enter requests AI details. Quick translation stays readable and copyable while AI loads or setup is missing. Settings opens the config and closes the popup. |

## Error logging checks

- In the tray, select **Open error log**. It opens `errors.log`, or the log folder if no
  errors have been recorded. Confirm that normal Shift taps and double taps still work.
- Exit through the tray and start again. No new unclean-exit report should appear.
- In a separate test copy with isolated log paths, force a background exception. The error
  report must include the exception and stack trace before the process exits. Force a stop
  without an exception, then start again: the report must say the cause is unknown.

Build `tests/Klava.CrashProbe`. Run its executable with an empty log folder and a mode:
`Klava.CrashProbe <log-folder> crash|clean|wait|ui`. It uses the app's error handlers and
logger without installing keyboard hooks. `crash` must exit with an error and save the full
exception in `errors.log`. `ui` must save the UI exception and exit successfully. `clean`
must clear the session marker. Stop a `wait` process only after `ready.txt` appears, then
run `clean` with the same folder; it must report the previous unclean exit.

## Chatter check with the diagnostic logger

`tools/kbdlog.py` logs Raw Input (what the keyboard sent) and hook events. Raw Input still
shows bounces when Klava runs, because Klava filters at the hook level. To see the effect of
the filter, compare the bounce lines in `klava.log` with the `CHATTER` lines of the logger.

To find the source of an unsupported key code, run
`python tools/kbdlog.py --unsupported-only --seconds 900`. This mode records only codes
outside 1–254, with scan codes, flags, hook injection details, and Raw Input device paths.
It does not record normal keys or layout changes. Try special keys on each keyboard and
device gestures separately, and note the action time. Match RAW and HOOK entries by time
and scan code. A hook event alone does not identify a device or the sending process.
The capture stops after 15 minutes. Tests: `python -m unittest discover -s tools -p test_kbdlog.py`.
