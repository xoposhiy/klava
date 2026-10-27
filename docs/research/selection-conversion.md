# Research: convert selected text

Date: 2026-10-27. Background for "Selected text" in `docs/spec.md`.

This research is an agent's reading of the source code and documentation, not a test on
a real machine. Claims marked "not verified" were not checked in the source. Some links in
"Sources" point to issues in unrelated repositories; treat the claims that depend only on
them (Chrome/Electron activation, nested TextPattern) as unverified.

## Summary

None of the open-source switchers found reads the selection through UI Automation (UIA)
on Windows. All Windows switchers send a copy shortcut and read the clipboard. Only macOS
switchers read the selection through the platform accessibility API, and they use the
clipboard as a fallback. FluentSwitcher has the same "double Shift converts the selection"
feature as Klava. It uses UIA only to detect password fields and to read the text before
the caret, not to read the selection.

## Open-source switchers with "convert selection"

### Mahou (C#)

Repo: gitea.com/BladeMight/Mahou (the GitHub mirror iamkarlson/Mahou is old).
Source: `Mahou/Classes/KMHook.cs` (`GetClipStr`, `MakeCopy`, `ConvertSelection`,
`PasteText`, `ReSelect`) and `NativeClipboard.cs`.

- Detects "nothing selected": empties the clipboard, sends a copy shortcut, and checks for
  a non-empty `CF_UNICODETEXT`. It sends Ctrl+Insert first and Ctrl+C only if that gave
  nothing. It never sends Ctrl+C to consoles, where Ctrl+C interrupts the running program:
  ```csharp
  KInputs.MakeInput(KInputs.AddPress(Keys.Insert)); ... Thread.Sleep(30);
  if (string.IsNullOrEmpty(txt)) {
    if (clsNM == "ConsoleWindowClass" || clsNM == "CASCADIA_HOSTING_WINDOW_CLASS" || clsNM == "mintty" || clsNM == "VirtualConsoleClass") { /* skip */ }
    else { /* Ctrl+C */ }
  ```
- Waits: a fixed 30 ms sleep, with optional retries. Before each copy it spins on
  `GetOpenClipboardWindow()` for up to 3000 tries. Qt windows get an extra 40 ms between
  Ctrl down and Insert.
- Backup: all formats except `CF_BITMAP`, `CF_ENHMETAFILE`, `CF_TEXT`, `CF_OEMTEXT`, plus
  one of DIB/DIBV5. There is also a text-only mode.
- Bug to avoid: `clip_set` calls `GlobalFree(alloc)` after `SetClipboardData`. The system
  owns that memory after the call.
- Writes the result: Backspace, then types the text with `KEYEVENTF_UNICODE` SendInput.
  An optional mode pastes with Ctrl+V. LibreOffice gets Ctrl+Shift+Alt+V. Word gets a
  special case that trims trailing newlines.
- Reselect: optional, with RShift+Left×N. Mahou uses MSAA
  (`AccessibleObjectFromWindow(OBJID_CARET)`) only for the caret position, not UIA.

### SimpleSwitcher (Aegel5/SimpleSwitcher, C++)

Source: `src/WorkerImplement.cpp`, `src/CClipWorker.h`, `src/HookerThread.h`.

- Does not empty the clipboard. Registers `AddClipboardFormatListener`, backs up the
  clipboard, sends Ctrl+C, and accepts a `WM_CLIPBOARDUPDATE` only within 500 ms. No update
  means nothing was selected.
- Backs up only `CF_UNICODETEXT`, `CF_HDROP`, `CF_DIB` and the shell formats, up to 600 KB.
- Types the result as VK codes over the selection (no Backspace). Restores the clipboard
  after 20 ms.

### FluentSwitcher (DmitryN71/FluentSwitcher, a fork of SimpleSwitcher)

The closest match to Klava: when there is no typed word, "Shift twice" converts the
selection.

- Paste instead of typing: it changed from typing to one Ctrl+V. The code comment says
  that "Windows 11 Notepad lost the first key ('эта' -> 'та')" and that per-character
  layout detection broke words.
- Scintilla guard: before the copy it asks Scintilla editors directly:
  ```cpp
  SendMessageTimeoutW(focus, 2650 /*SCI_GETSELECTIONEMPTY*/, 0, 0, SMTO_ABORTIFHUNG, 200, &empty)
  ```
  The comment says Notepad++ copies the whole line on an empty selection, which gave
  "GhbdtnПривет".
- Backup: keeps formats in their original order (text, locale, files, HTML, RTF, PNG, DIB,
  up to 32 MB). It does not read private formats: reading Excel's `ShadowWorkbook` breaks
  the paste of cut rows.
- Clipboard history: the temporary text carries `ExcludeClipboardContentFromMonitorProcessing`,
  `CanIncludeInClipboardHistory = 0` and `CanUploadToCloudClipboard = 0`.
- Restore: 250 ms after the paste.
- UIA: with `ConnectionTimeout`/`TransactionTimeout` of 500 ms, only for `IsPassword` and
  the text before the caret (`src/FieldText.h`). A comment says this was checked on Edit,
  RichEdit and a Chromium/ProseMirror field.

### dotSwitcher (kurumpa, C#)

The naive version: Ctrl+C, a 20 ms sleep, then `Clipboard.GetText()`.
`BackupClipboard`/`RestoreClipboard` are commented out, so old clipboard text gets
converted when nothing is selected.

### PolterType (Rust: Windows, macOS, Linux)

Source: `crates/poltertype-core/src/engine/switcher/correction.rs`.

- Stores the previous clipboard text instead of writing a sentinel. Sends Ctrl+C and polls
  every 20 ms for up to 400 ms until the text differs from the previous text.
- Pastes with Ctrl+V and restores after `PASTE_SETTLE = 250ms`. Saves and restores text only.
- Releases held modifiers first: a held Shift turns Ctrl+C into Ctrl+Shift+C, and the
  switcher then reads that as "nothing selected".

### xneur (Linux)

Source: `xneur/lib/main/selection.c`, `program.c`. Reads the X11 PRIMARY selection with
`XConvertSelection(UTF8_STRING)` and never touches the clipboard. Types keycodes over the
selection with no Backspace. With the option on, reselects with Left×N, then Shift+Right×N.

### AutoHotkey scripts

Forum topics 24666 and 90089 (not read on GitHub). The pattern: `saved := ClipboardAll`,
clear the clipboard, `^c` or `^{Ins}`, `ClipWait`, map the characters, `SendInput {Del}text`
or `^v`, restore. Some posts add `Sleep 500` to fix timing.

### macOS switchers (accessibility API first, clipboard as fallback)

- keyboop (`Sources/Keyboop/SelectionText.swift`, `Engine.swift`):
  - Reads `kAXSelectedTextAttribute`. If that fails, sends Cmd+C and waits on the clipboard
    change counter for up to 250 ms.
  - Snapshots every clipboard type in order and marks the restored data as transient, so
    clipboard managers ignore it.
  - Writes through `AXUIElementSetAttributeValue(kAXSelectedText)` and reads the value back.
    Firefox is a special case: its accessibility write corrupts the text around the
    selection, so keyboop types there instead.
  - In the clipboard path it rejects text with more than one line or more than 80
    characters: that is "probably an auto-copy of the line".
  - Sets a 0.25 s accessibility timeout and never calls the accessibility API from the
    event tap.
- Klavko (`SelectionConverter.swift`): tries the accessibility write first. If that fails,
  it pastes, reselects, and restores the clipboard only if the change counter has not
  changed since its own write.

## Reading the selection without the clipboard on Windows

### UI Automation

Call chain: `GetFocusedElement` → `IsPassword` check → `GetCurrentPatternAs(TextPattern)`
→ `GetSelection` → `GetText(maxLen)`. An empty selection range is a confirmed "nothing
selected". TextPattern is read-only: the result still must be typed or pasted.
`ValuePattern.SetValue` replaces the whole field and breaks undo.

| App | TextPattern selection | Source / how sure |
|---|---|---|
| Win32 Edit / RichEdit (classic Notepad, WinForms) | Yes | FluentSwitcher comment. Native RichEdit support was missing on Win7 (MS Q&A). |
| Win11 Notepad, Word, Outlook, WPF, WinUI/UWP | Expected yes | Not verified. Check with Inspect.exe. |
| Chrome/Edge | Native UIA by default from Chrome 138 (rollout from 126) | Chrome blog. Chromium turns accessibility on lazily: the tree can appear about 50 ms after the first read, and turning it on slows the browser. An empty child TextPattern can hide the selection of its parent. |
| Electron (VS Code, Slack) | Only with recent Chromium | NVDA issue #19276 says on by default from Chromium 142. Older versions go through the MSAA proxy, probably with no TextPattern (not verified). |
| Firefox | Native UIA with TextPattern in current source (`accessibility.uia.enable=2`) | Checked in StaticPrefList.yaml and uiaRawElmProvider.cpp. Release version unknown. |
| Qt 5.11+/6 (Telegram) | Qt has UIA text providers (`qwindowsuiatextprovider.cpp`) | Telegram's custom widgets not verified. |
| JetBrains IDEs | No UIA, only Java Access Bridge (turned on with `jabswitch`) | JetBrains docs. |
| Windows Terminal / conhost | TextPattern exists | Replacing a terminal selection makes no sense. |

Cost and hangs:

- Every UIA call is a cross-process round trip. The default connection timeout is 2 s.
- Make UIA calls from an MTA thread that owns no windows, never from the UI thread or the
  low-level hook callback.
- Set `IUIAutomation2.ConnectionTimeout`/`TransactionTimeout` to about 300–500 ms.
- .NET 10: `System.Windows.Automation` needs `<UseWPF>true</UseWPF>`. Without WPF, use the
  COM API (`Interop.UIAutomationClient`, FlaUI.UIA3, a `COMReference`, or hand-written
  `[ComImport]` interfaces) and `CUIAutomation8` for `IUIAutomation2`.

### EM_GETSEL + WM_GETTEXT

- Both messages are below `WM_USER`, so Windows marshals them across processes.
  `EM_EXGETSEL` and `EM_GETSELTEXT` are not marshaled. `EM_REPLACESEL` can replace the text
  and keep undo; `EM_SETSEL` can reselect.
- RichEdit counts CR as one position, but `WM_GETTEXT` returns CRLF, so offsets differ.
- Use only for the `Edit` and `RichEdit*` classes, with
  `SendMessageTimeout(SMTO_ABORTIFHUNG)`. Not checked for Win11 Notepad (`RichEditD2DPT`).

## Clipboard path: details worth copying

- Do not empty the clipboard first. Record `GetClipboardSequenceNumber()`, make sure no
  modifier is down, send Ctrl+C, and poll the number every 10–20 ms for up to about 400 ms.
  No change means nothing was selected.
- Back up formats in their original order. Back up only standard formats (text, locale,
  HTML, RTF, DIB, PNG, HDROP) with a size cap. Do not read private formats.
- Formats that mark a whole-line copy on an empty selection: `MSDEVLineSelect` (Scintilla)
  and `VisualStudioEditorOperationsLineCutCopyClipboardTag` (Visual Studio), both verified;
  VS Code's `vscode-editor-data` JSON with `isFromEmptySelection` (on Windows it probably
  sits inside Chromium's custom data format; not verified).
- Reject copied text that ends with a newline or has more than one line.
- Never send Ctrl+C to console windows: `ConsoleWindowClass`, `CASCADIA_HOSTING_WINDOW_CLASS`,
  `mintty`, `VirtualConsoleClass`.
- Paste with Ctrl+V. Mark the temporary text with the three history formats. Restore the
  clipboard about 250 ms after the paste, and only if the sequence number is still the one
  from Klava's own write.

## Risks

- Apps that copy the whole line on an empty selection cause false conversions.
- Clipboard races with clipboard managers and Win+V history. The target app's own Ctrl+C
  result always goes into the history; Klava cannot mark it.
- A restore that comes too early makes the app paste the old content.
- Elevated windows block both SendInput and UIA from a non-elevated Klava.
- Clipboard and UIA calls can hang: use timeouts and never block the hook thread.

## Sources

- Switchers: [Mahou (Gitea)](https://gitea.com/BladeMight/Mahou),
  [SimpleSwitcher](https://github.com/Aegel5/SimpleSwitcher),
  [FluentSwitcher](https://github.com/DmitryN71/FluentSwitcher),
  [dotSwitcher](https://github.com/kurumpa/dotSwitcher),
  [PolterType](https://github.com/Just-Code-NET/PolterType),
  [xneur](https://github.com/AndrewCrewKuznetsov/xneur-devel),
  [keyboop](https://github.com/iffuno/keyboop), [Klavko](https://github.com/stepsergius/Klavko),
  [AHK forum topic 24666](https://www.autohotkey.com/board/topic/24666-keyboard-layout-switcher/).
- Browsers and editors: [Chrome UIA update](https://developer.chrome.com/blog/windows-uia-support-update),
  [Chromium a11y performance](https://developer.chrome.com/blog/chromium-accessibility-performance),
  [NVDA #19276](https://github.com/nvaccess/nvda/issues/19276),
  [Electron UIA activation issue](https://github.com/Yu-tao-Li/dsh-computer-use-win/issues/50),
  [Chromium nested TextPattern issue](https://github.com/tzachbon/harness-discussion/issues/81),
  [VS Code PR #333909](https://github.com/microsoft/vscode/pull/333909).
- Windows and Java: [UIA threading](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading),
  [ConnectionTimeout](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomation2-put_connectiontimeout),
  [Raymond Chen on message ranges](https://devblogs.microsoft.com/oldnewthing/20031202-00/?p=41653),
  [JetBrains accessibility](https://www.jetbrains.com/help/rider/Accessibility.html),
  [Interop.UIAutomationClient](https://www.nuget.org/packages/Interop.UIAutomationClient).
