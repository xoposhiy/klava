# Changelog

## 0.1.0 (unreleased)

First public version.

- Keep automated UI checks invisible during tests and publishing, including native DPI checks.
- Preserve candidate sorting, selection, and scroll position when Typing episodes refreshes.
  Keep unchanged tables, dictionary lists, and patterns intact. Keep dictionary input errors
  visible, disable approval for hidden candidates, and keep analysis disabled while it runs.
  Preserve Chatter history selection and scroll position; refresh idle Last hour once per minute.
- Fix the first tray menu selection leaving the Typing episodes window hidden after
  a publish with `-Restart`.
- Show each English candidate beside its Russian keyboard spelling. Show both spellings
  for approved words too.
- Find layout mistakes in a command that was edited with Home and Delete after its
  arguments were typed or pasted. Label evidence with a missing first press as partial.

- Exclude discarded Shift-Shift input from temporary-layout dictionary candidates. Do not
  treat edits to later text or actions after navigation as evidence of a failed correction.
  Show new candidates separately from the complete Dictionary tab.

- Collect local typing episodes around edits, Shift gestures, layout changes, mouse actions,
  and automatic corrections. Keep raw events for 7 days with an expiry notice; keep aggregate
  usage and versioned analysis results. Recognized password fields are excluded.
- Correct approved RU-layout words at word boundaries without changing the active layout.
  The dictionary starts empty and has one stored source. Open Typing episodes from the tray
  to run analysis manually, confirm candidates, or view and add words in the Dictionary tab.
  No built-in word list or typing.words config setting exists.

- Use GPT-6 Luna with reasoning effort set to none for AI translation details.

- Use a simple light translation window with a standard Windows frame, native language
  menus, and text buttons sized to their captions. Scale the complete layout before showing
  it, including at 150% Windows scaling. Keep buttons aligned after resizing and reopening.
  Show clear text sizes, formatted word meanings, and detected language names;
  keep quick translations visible while AI details load, and use shorter setup hints.
  Drag the header to move the window, resize from its edges, press Ctrl+C to copy the result,
  or Ctrl+Enter for AI details. Copy confirms success in the button.

- Translate selected words and phrases with Ctrl+Left Alt+T and DeepL. Default directions:
  EN → RU, RU → EN, DE → RU; choose any EN/RU/DE pair in the result window. Restore the
  clipboard after capture. An optional AI details button requests word senses,
  examples, or alternative phrase translations through OpenAI. Configure `translation.deeplKey`
  and, for AI details, `translation.openaiKey`. Neither text nor results are saved in logs.

- Chatter filter with a global threshold and `chatteringKeys` for keys that chatter slower.
- Layout switcher: single Shift tap switches the layout, double tap retypes the last word.
- Double Shift tap with no typed word converts the selected text (up to `maxSelectionChars`,
  default 20) through the clipboard, and then restores the clipboard. Skips console windows
  and whole-line copies of an empty selection in Notepad++, Visual Studio, and VS Code.
- AutoHotkey-style hotkeys for Unicode symbols and key sequences.
- Tray menu with feature toggles, bounce stats, and the fastest passed re-presses.
- Log of blocked bounces, fast re-presses, hotkey sends, and an opt-in key trace.
- Diagnostic logger mode `--unsupported-only` to find the source of unmapped key events
  without recording normal keys.
- Saved daily chatter history by key, with charts for event counts and re-press gaps.
  Open the window from `Chatter history...` in the tray.
- Press totals and blocked/fast-passed percentages in chatter history, with Today and Last hour periods.
  Start with complete press counts; do not import old log entries.
- Safety net that releases a modifier that Windows still reports down after its release.

Fixed during development:

- Keep input processing fast as chatter history grows: publish display snapshots only on
  request, merge old minutes once per minute, and save counts without copying all gap arrays.
- Keep tray menu sorting, text formatting, and control updates outside the hook's stats lock.
- Treat null config sections and list items, invalid send Unicode codes, and incomplete send
  strings as config errors. Keep working settings on reload and use defaults on startup.
- Save errors in a separate durable error log, with fallback files when the main path is
  unavailable. Save fatal exceptions without waiting for the background log worker. Report
  unclean sessions on the next start and add an Open error log tray item.
- Wait for log writes to finish when flushing. Keep the log worker alive after write failures.
- Reject invalid history events, bound the history queue, and isolate unexpected history
  worker failures so statistics cannot close Klava or delay keyboard input.

- An unmapped key event (`0xFF`), reproduced with the built-in Lenovo Fn key, could crash
  the chatter history worker and close Klava.
  Pass these events through without processing or recording them.

- Double Shift could retype a lowercase word in uppercase because Klava's Caps Lock count
  differed from Windows. Read the Windows toggle state for each typed key.

- Chatter history mixed older event counts with rates from newer presses. Show matching counts
  and rates from the same recorded presses. Remove support for older incomplete counts.
  Show positive rates below 0.01% without rounding them to zero.

- Chatter history text and charts could overlap at higher display scaling. Scale the window
  from 96 DPI, fit text rows to their content, and measure chart labels before placing them.

- Two bounces in one press typed the letter twice: the filter measured the second bounce from
  the first release, not from the release of the blocked bounce.
- AltGr layouts (ru-RU on Windows 10+): Left Ctrl stayed down after a Right Alt hotkey.
- A missed Right Alt release (elevated window, lock screen) turned every later `x` into `✕`.
- After a Right Alt hotkey, each Left Alt press opened ChatGPT AppShots (hotkey Alt + Alt):
  Klava blocked the physical release of the modifiers it had released, and ChatGPT skips
  Klava's injected release, so it saw Right Alt down until the next Right Alt press.
- Synthetic End, Home and arrows were sent without the extended-key flag (apps saw numpad keys).
- Cyrillic text from the layout switcher was cut to single bytes.
