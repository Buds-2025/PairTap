# 对符输入 PairTap

在 Windows 上选中文本，按快捷键一键在两端加上成对的引号、括号或书名号。

[下载 PairTap.exe（绿色单文件）](https://github.com/Buds-2025/PairTap/releases/latest/download/PairTap.exe) · [English](README.en.md) · [Releases](https://github.com/Buds-2025/PairTap/releases/latest)

---

## 使用方法

1. **运行**：下载并打开 `PairTap.exe`（单文件免安装，运行后常驻屏幕右下角系统托盘）。
2. **选中**：在 Word、文本编辑器或网页输入框的可编辑区域中**选中文本**。
3. **加标点**：
   - 直接按**快捷键**包裹（如 `Ctrl+Shift+'` 加双引号）；
   - 或按 `Ctrl+Alt+F6` 打开面板点击选用。

![PairTap 标点面板](assets/pairtap-panel.png)

---

## 常用快捷键

平时打标点按什么键，加按一个 `Ctrl` 就是对应的对符快捷键：

| 选中的文本 | 快捷键 | 中文标点效果 | 半角标点效果 |
| :--- | :--- | :--- | :--- |
| 稍后确认 | `Ctrl+Shift+'` | `“稍后确认”` | `"稍后确认"` |
| 原文 | `Ctrl+'` | `‘原文’` | `'原文'` |
| 可选 | `Ctrl+Shift+9` 或 `Ctrl+Shift+0` | `（可选）` | `(可选)` |
| 待处理 | `Ctrl+[` 或 `Ctrl+]` | `【待处理】` | `[待处理]` |
| 备注 | `Ctrl+Shift+[` 或 `Ctrl+Shift+]` | `「备注」` | `{备注}` |
| 小王子 | `Ctrl+Shift+,` 或 `Ctrl+Shift+.` | `《小王子》` | `<小王子>` |

> **提示**：
> - 按 `Ctrl+Alt+F6` 可选用全部 23 组标点（包括各类括号、书名号、直角引号等）。
> - 右键托盘图标点击「快捷键设置」，可修改/禁用快捷键，或勾选「固定中文标点」。
> - 在 Word 正文和表格中，标点会直接插入选区两侧，保留原文格式，一次撤销即可移除两侧标点。
> - 运行日志位于 `%LOCALAPPDATA%\PairTap\Logs\runtime.log`，仅记录操作状态、耗时和错误代码。
