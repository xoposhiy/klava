# Klava

A small Windows tray app that fixes and extends keyboard input with one low-level keyboard hook:

- **Chatter filter.** Drops phantom key presses from worn switches or loose hot-swap sockets:
  doubled letters, a Shift that "taps twice".
- **Layout switcher.** Tap Shift once to switch the keyboard layout. Tap Shift twice to retype
  the last word in the other layout: `ghbdtn` → `привет`. With no word typed, two taps convert
  the selected text through the clipboard. Like Caramba Switcher or Punto Switcher, without
  automatic switching.
- **Hotkeys.** AutoHotkey-style rules: Right Alt + key types a Unicode symbol
  (`✕ ≤ ≥ α β π — « » → ²` and more), Win+Ctrl+arrows go to Home/End.
- **Selection translation.** Ctrl+Left Alt+T opens a quick DeepL translation. An optional
  AI details button gives word senses and examples or alternative phrase translations.
  Supports English, Russian, and German.

All three work in one hook and in a fixed order, so the chatter filter removes a phantom Shift
press before the layout switcher can see it. Separate tools cannot guarantee that.

## Install

1. Install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Download `Klava-<version>-win-x64.zip` from the releases page, unzip it, and start `Klava.exe`.
   Or build it yourself: `powershell -File scripts/publish.ps1`, then start `dist\Klava.exe`.
3. Close other layout switchers (Caramba Switcher, Punto Switcher) and AutoHotkey scripts
   that use the same keys. Klava warns at start if they run.
4. In the tray menu, check **Start with Windows**.

## Configure

The config file is `%APPDATA%\Klava\config.json`. Klava creates it on the first run. Open it
from the tray menu, edit it, and click **Reload config**. Comments in the file explain each
setting; [docs/spec.md](docs/spec.md) has the exact behavior.

### Chatter filter

A press that comes sooner than `thresholdMs` (default 30 ms) after the release of the same key
is dropped. Bounces are usually 1–15 ms apart; real fast re-presses of one key are 45+ ms apart.

If one key still doubles, give it its own threshold:

```jsonc
"debounce": {
  "thresholdMs": 30,
  "chatteringKeys": { "M": 50, "LShift": 40 }
}
```

To find such keys, open the tray menu: it shows the blocked bounces per key and the fastest
re-presses that passed the filter. The log (`%LOCALAPPDATA%\Klava\klava.log`) has a line for
each of them. For a detailed trace of a few keys with their neighbor keys and pauses, set
`"debug": { "traceKeys": ["M", "LShift"] }`; the trace contains fragments of typed text, so
turn it off when you are done.

### Layout switcher

```jsonc
"switcher": {
  "maxTapMs": 500,                // a longer Shift hold is not a tap
  "doubleTapMs": 400,             // max pause between the two taps of a double tap
  "maxSelectionChars": 20,        // longest selected text to convert; 0 turns it off
  "layouts": ["en-US", "ru-RU"],  // layouts to cycle
  "switchMethod": "Message"       // or AltShift, CtrlShift, WinSpace if an app ignores Message
}
```

### Hotkeys

Triggers use AutoHotkey syntax: `#` Win, `^` Ctrl, `!` Alt, `+` Shift, `<` and `>` for the left
or right key, `<^>!` for AltGr. An action is `text` or `send` (`^{End}`, `{U+2715}`):

```jsonc
{ "keys": [">!x", "<^>!x"], "text": "✕" },
{ "keys": ["#^Right"], "send": "{End}" }
```

Add the `<^>!` trigger next to `>!`: on layouts with AltGr (for example, Russian on Windows 10
and later), Right Alt reaches Klava as Left Ctrl + Right Alt.

### Selection translation

Add this section to the config and click **Reload config**:

```jsonc
"translation": {
  "enabled": true,
  "hotkey": "^<!t",
  "deeplKey": "YOUR_DEEPL_API_KEY",
  "openaiKey": "",
  "model": "gpt-6-luna",
  "maxChars": 5000
}
```

Select a word or phrase in an app, then press **Ctrl+Left Alt+T**. Klava copies the selection,
restores the clipboard, and opens the translation near the pointer. The source text stays in
the original app. Esc closes the window. Hold repeats do not start more requests.

With both language menus set to Auto: EN → RU, RU → EN, DE → RU. Use the language menus
to select any pair of EN, RU, and DE. For short ambiguous words, choose the source explicitly:
for example, English `gift` and German `Gift` have different meanings.

DeepL provides the initial translation. **AI details** makes a separate OpenAI request using
`gpt-6-luna` with `reasoning.effort` set to `none` by default. For a word, it shows common
senses, parts of speech, translation equivalents, and examples; for a phrase, it shows
alternative translations when context allows different meanings. Set `openaiKey` to use this
button. AI details may be incomplete or wrong and are not entries from a published dictionary.
The button uses DeepL's detected direction unless the language selectors specify a direction.

Empty keys use the `DEEPL_API_KEY` and `OPENAI_API_KEY` environment variables. API access
uses the providers' API accounts. Requests send only the selected text, only after the hotkey
(DeepL) or the details button (OpenAI). Klava does not log or save source text or translations;
up to 100 results per provider are cached in memory until reload or exit. Copy result replaces
the clipboard only when clicked. The Hotkeys toggle also controls the translation hotkey.

The popup uses a simple light window with a standard Windows title bar and resize border.
Long results scroll with the wheel, keyboard, or scroll bar. Press Ctrl+C to copy the result
(or selected text in the focused text control), Ctrl+Enter for AI details, and Esc to close.
The settings button opens the config. Missing API setup shows a short hint; hover over it for
the field name and config path.

Terminal windows are skipped because Ctrl+C can stop a command. Apps must support copying
selected text with Ctrl+C. Clipboard preservation has the same format limits as selection
layout conversion; the app's Ctrl+C copy may enter clipboard history. An editor that copies an
unmarked whole line with no selection can cause that line to be translated.

## Is it the keyboard or the driver?

`tools/kbdlog.py` (Python 3, no packages needed) logs every key event from Raw Input, which
shows what the device sent, and from a keyboard hook. Run `python tools/kbdlog.py`, type, and
look for `CHATTER` lines. If the chatter also shows up over a different connection (USB and
Bluetooth), the keyboard is the cause.

## Limits

- Klava does not see input for elevated (administrator) windows unless it runs elevated too.
- Klava filters at the hook level. Apps that read Raw Input directly (some games and some
  global-hotkey tools) still see the bounces.

## Build and contribute

```
dotnet build
dotnet test
```

Read [AGENTS.md](AGENTS.md) for the architecture rules, [docs/spec.md](docs/spec.md) for the
behavior, and [docs/manual-tests.md](docs/manual-tests.md) for the checks that unit tests cannot
cover.

## License

[MIT](LICENSE)
