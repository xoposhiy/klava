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
- Safety net that releases a modifier that Windows still reports down after its release.

Fixed during development:

- Two bounces in one press typed the letter twice: the filter measured the second bounce from
  the first release, not from the release of the blocked bounce.
- AltGr layouts (ru-RU on Windows 10+): Left Ctrl stayed down after a Right Alt hotkey.
- A missed Right Alt release (elevated window, lock screen) turned every later `x` into `✕`.
- Synthetic End, Home and arrows were sent without the extended-key flag (apps saw numpad keys).
- Cyrillic text from the layout switcher was cut to single bytes.
