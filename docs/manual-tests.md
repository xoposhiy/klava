# Manual tests

Unit tests cannot prove how the hooks behave in real apps. Run these checks after any change in
`src/Klava.App`, and the checks for the feature you changed after a change in `src/Klava.Core`.

## Setup

1. Close Caramba Switcher and AutoHotkey.
2. Run `dotnet run --project src/Klava.App`, or start `dist\Klava.exe`.
3. Check that the tray shows the blue "K" icon and that `%LOCALAPPDATA%\Klava\klava.log` has
   a `Started` line.

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
| 12 | Tray: uncheck Enabled | Icon turns gray; Shift taps and hotkeys do nothing. |
| 13 | Edit config with an error, Reload config | A warning balloon; the old config keeps working. |

## Chatter check with the diagnostic logger

`tools/kbdlog.py` logs Raw Input (what the keyboard sent) and hook events. Raw Input still
shows bounces when Klava runs, because Klava filters at the hook level. To see the effect of
the filter, compare the bounce lines in `klava.log` with the `CHATTER` lines of the logger.
