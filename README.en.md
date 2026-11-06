# PairTap — Wrap Selected Text in Quotes & Brackets on Windows

**A lightweight Windows shortcut tool to wrap selected text in quotes, brackets, and title marks with a single keystroke.**

Select any text in an editor, document, or browser input, press a shortcut, and PairTap places matching punctuation at both ends. For example, select `check later` and press a shortcut to instantly get `“check later”`, `"check later"`, or `(check later)`.

[Download PairTap.exe (Portable)](https://github.com/Buds-2025/PairTap/releases/latest/download/PairTap.exe) · [简体中文](README.md) · [Release Notes & SHA-256](https://github.com/Buds-2025/PairTap/releases/latest)

---

## Why PairTap Exists

We've all been there while typing: you've finished typing a phrase or sentence, only to realize you forgot to enclose it in quotes, parentheses, or book title marks.

The traditional workaround is tedious:
- Either carefully click to place the caret at the start, type the opening mark, then move to the end and type the closing mark;
- Or cut the selection, type both marks, and paste the text back inside.

Sure, there are alternatives—like writing a Word macro or using an existing [Quicker "超级对符" (Super Pair)](https://www.getquicker.net/Sharedaction?code=0cf35569-18d3-4ac6-99bf-08dcff9008ea) action. But macros only work within a specific app, and Quicker requires keeping a bulky runtime running. Now that we have AI coding assistants, I decided to build my own: a dead-simple, standalone, native Windows tool. Zero setup, portable single-executable, one keystroke to get it done.

---

## Key Features

- **One-Keystroke Wrapping**: Select text and press a shortcut—no manual cursor positioning or copy-pasting.
- **Intuitive Shortcuts**: Press the key you usually use to type the mark, plus `Ctrl` (e.g. `'` types a quote, `Ctrl+'` wraps in single quotes; `Shift+'` types a double quote, `Ctrl+Shift+'` wraps in double quotes).
- **Ultra Lightweight**: Single portable `.exe` under 100KB, native .NET, near-zero memory footprint, runs from the tray without installation.
- **Clipboard Protection**: When falling back to clipboard-based replacement, PairTap **automatically backs up and restores your clipboard**, never losing your existing copied content.
- **23-Pair Visual Panel**: Press `Ctrl+Alt+F6` anytime to pop up a clean panel covering quotation marks, brackets, book title marks, and half-width symbols.
- **Offline & Private**: No network calls, no telemetry, no admin privileges required.

---

## Quick Start (30 Seconds)

1. **Download & Run**: Download [`PairTap.exe`](https://github.com/Buds-2025/PairTap/releases/latest/download/PairTap.exe) and double-click to run. The PairTap icon appears in your system tray.
2. **Select Text**: Highlight any text in your editor, document, or browser input.
3. **Wrap**:
   - **Via Shortcut**: Press `Ctrl+Shift+'` to wrap in `“selected text”`.
   - **Via Panel**: Press `Ctrl+Alt+F6` to pop up the punctuation palette, then click your desired pair.

![PairTap punctuation panel on Windows](assets/pairtap-panel.png)

---

## Default Shortcuts

On standard US keyboard layouts, the most common pairs have direct global shortcuts:

| Selection | Default Shortcut | Chinese Punctuation | Half-Width Punctuation |
| :--- | :--- | :--- | :--- |
| check later | `Ctrl+Shift+'` | `“check later”` | `"check later"` |
| original | `Ctrl+'` | `‘original’` | `'original'` |
| optional | `Ctrl+Shift+9` or `Ctrl+Shift+0` | `（optional）` | `(optional)` |
| pending | `Ctrl+[` or `Ctrl+]` | `【pending】` | `[pending]` |
| note | `Ctrl+Shift+[` or `Ctrl+Shift+]` | `「note」` | `{note}` |
| The Little Prince | `Ctrl+Shift+,` or `Ctrl+Shift+.` | `《The Little Prince》` | `<The Little Prince>` |

> **Tip**: Press `Ctrl+Alt+F6` to open the panel and choose from all **23 matching pairs**.

### Supported Punctuation Pairs (23 Pairs)

| Category | Supported Pairs |
| :--- | :--- |
| **Quotation Marks** | `“ ”`, `‘ ’`, `「 」`, `『 』`, `« »`, `‹ ›`, `＂ ＂`, `＇ ＇` |
| **Chinese Brackets & Title Marks** | `（ ）`, `【 】`, `〖 〗`, `〔 〕`, `《 》`, `〈 〉`, `［ ］`, `｛ ｝`, `＜ ＞` |
| **Half-Width Punctuation** | `( )`, `[ ]`, `{ }`, `< >`, `" "`, `' '` |

---

## FAQ

### Q: Does it work in Word, browsers, WeChat, or code editors?
**Yes.**
PairTap employs a dual-engine approach:
- **Standard text controls** (Notepad, standard edit controls): Directly replaced via Win32 API in memory, instant and without touching the clipboard.
- **Modern complex applications** (Chrome/Edge, Word, WeChat, etc.): Handled via UI Automation and input simulation.
*Note: In rich-text editors (like formatted text in Word), wrapping replaces text with plain text, so internal formatting (bold/color) inside the selection may revert to plain text. Password fields and elevated admin windows may be restricted by Windows security policies.*

### Q: Will it overwrite my clipboard?
**No.**
When PairTap uses the clipboard as a fallback, it automatically backs up your existing clipboard contents first and restores them right after wrapping. Your previously copied code, text, or images stay intact.

### Q: How do I choose between Chinese quotes (`“ ”`) and ASCII quotes (`" "`)?
By default, PairTap detects your Windows input method status.
If you always want Chinese quotation marks regardless of input method mode:
- Right-click the tray icon and select "快捷键设置" (Shortcut Settings), or open settings from the panel.
- Check "固定中文标点" (Force Chinese Punctuation), or bind a dedicated shortcut directly to `“ ”`.

### Q: What if a shortcut conflicts with another app?
Open Shortcut Settings from the tray icon or the bottom-right of the panel. You can customize any keybinding, clear bindings you don't need, or reset to defaults. Settings are saved per-user in the registry and persist across updates.

### Q: How does it compare to Word macros or Quicker?
- **vs. Word Macros**: PairTap is a system-wide global tool—it works everywhere, not just Word (browsers, chat apps, Markdown editors, spreadsheets, web forms).
- **vs. Quicker Actions**: No need to install and run the Quicker client environment. PairTap is zero-dependency, standalone, under 100KB, portable, and instant.

---

## Build from Source & Feedback

PairTap is built with native C# and .NET Framework without needing heavy build toolchains.

On Windows 11 / 10, simply run the PowerShell script from the repository root:
```powershell
.\Build.ps1
```
The script calls the built-in .NET Framework compiler and outputs `dist\PairTap.exe`.

Questions or suggestions? Feel free to [Open an Issue](https://github.com/Buds-2025/PairTap/issues/new).
