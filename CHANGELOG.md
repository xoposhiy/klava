# Changelog

## 0.1.0 (unreleased)

First public version.

- Chatter filter with a global threshold and `chatteringKeys` for keys that chatter slower.
- Layout switcher: single Shift tap switches the layout, double tap retypes the last word.
- Double Shift tap with no typed word converts the selected text (up to `maxSelectionChars`,
  default 20) through the clipboard, and then restores the clipboard. Skips console windows
  and whole-line copies of an empty selection in Notepad++, Visual Studio, and VS Code.
- AutoHotkey-style hotkeys for Unicode symbols and key sequences.
- Tray menu with feature toggles, bounce stats, and the fastest passed re-presses.
- Log of blocked bounces, fast re-presses, hotkey sends, and an opt-in key trace.
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
