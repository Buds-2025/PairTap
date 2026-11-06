# PairTap

Wrap selected text in matching quotes, brackets, and title marks with a single shortcut on Windows.

[Download PairTap.exe (Portable)](https://github.com/Buds-2025/PairTap/releases/latest/download/PairTap.exe) · [简体中文](README.md) · [Releases](https://github.com/Buds-2025/PairTap/releases/latest)

---

## How to Use

1. **Run**: Download and run `PairTap.exe` (portable single-file, sits in the system tray).
2. **Select**: Highlight text in an editable area in Word, a text editor, or a browser.
3. **Wrap**:
   - Press a **shortcut** (e.g. `Ctrl+Shift+'` for double quotes);
   - Or press `Ctrl+Alt+F6` to pick a pair from the visual panel.

![PairTap Punctuation Panel](assets/pairtap-panel.png)

---

## Default Shortcuts

Add `Ctrl` to your normal punctuation keystroke to wrap the selected text:

| Selection | Shortcut | Chinese Marks | Half-Width Marks |
| :--- | :--- | :--- | :--- |
| check later | `Ctrl+Shift+'` | `“check later”` | `"check later"` |
| original | `Ctrl+'` | `‘original’` | `'original'` |
| optional | `Ctrl+Shift+9` or `Ctrl+Shift+0` | `（optional）` | `(optional)` |
| pending | `Ctrl+[` or `Ctrl+]` | `【pending】` | `[pending]` |
| note | `Ctrl+Shift+[` or `Ctrl+Shift+]` | `「note」` | `{note}` |
| The Little Prince | `Ctrl+Shift+,` or `Ctrl+Shift+.` | `《The Little Prince》` | `<The Little Prince>` |

> **Tips**:
> - Press `Ctrl+Alt+F6` to choose from all 23 matching pairs.
> - Click the tray icon to view the punctuation panel. Right-click and choose Shortcut Settings to customize shortcuts or lock output to Chinese marks.
> - In Word documents and tables, PairTap inserts delimiters around the selection, preserving its formatting. A single undo removes both delimiters.
> - The runtime log at `%LOCALAPPDATA%\PairTap\Logs\runtime.log` records operation status, timing, and error codes.
