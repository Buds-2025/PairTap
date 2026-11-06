# 对符输入 PairTap | Windows 选中文字一键加引号、括号和书名号

**对符输入（PairTap）** 是一款 Windows 托盘工具。选中已有文字，按一个快捷键，就能在两端同时加上引号、括号或书名号。例如，可以直接把 `人工智能` 变成 `“人工智能”`、`（人工智能）` 或 `《人工智能》`。

[下载安装版](https://github.com/Buds-2025/PairTap/releases/latest/download/PairTap-Setup.exe) · [下载便携版](https://github.com/Buds-2025/PairTap/releases/latest/download/PairTap.exe) · [查看版本和 SHA-256 校验值](https://github.com/Buds-2025/PairTap/releases/latest)

![对符输入的 Windows 标点面板，显示中文引号、括号、书名号和半角符号](assets/pairtap-panel.png)

## 怎么给选中的文字一次加上两边的标点？

1. 下载并运行 `PairTap-Setup.exe`。安装完成后，工具会出现在系统托盘。也可以直接运行便携版 `PairTap.exe`。
2. 在编辑器或网页输入框里选中一段可编辑的文字。
3. 按 `Ctrl+Alt+F6` 打开标点面板，点击要加的符号。也可以按下表中的快捷键直接包裹选区。

安装后可从 Windows 开始菜单搜索「对符输入」。卸载入口在「设置 → 应用 → 已安装的应用」。安装版和便携版目前均未做代码签名。

## 默认快捷键与示例

下表按美式键盘布局列出。直接加标点的快捷键，通常是在原来的符号键组合上再按住 Ctrl。例如，双引号原来按 `Shift+'`，用对符输入时按 `Ctrl+Shift+'`。

| 操作 | 默认快捷键 | 中文标点示例 | 半角示例 |
| --- | --- | --- | --- |
| 打开标点面板 | `Ctrl+Alt+F6` | 从23种符号中选择 | 从23种符号中选择 |
| 加双引号 | `Ctrl+Shift+'` | `“人工智能”` | `"人工智能"` |
| 加单引号 | `Ctrl+'` | `‘人工智能’` | `'人工智能'` |
| 加圆括号 | `Ctrl+Shift+9`、`Ctrl+Shift+0` | `（人工智能）` | `(人工智能)` |
| 方括号键 | `Ctrl+[`、`Ctrl+]` | `【人工智能】` | `[人工智能]` |
| 花括号键 | `Ctrl+Shift+[`、`Ctrl+Shift+]` | `「人工智能」` | `{人工智能}` |
| 尖括号键 | `Ctrl+Shift+,`、`Ctrl+Shift+.` | `《人工智能》` | `<人工智能>` |

### 支持哪些成对符号？

| 分类 | 符号 |
| --- | --- |
| 引号 | `“ ”`、`‘ ’`、`「 」`、`『 』`、`« »`、`‹ ›`、`＂ ＂`、`＇ ＇` |
| 中文括号与书名号 | `（ ）`、`【 】`、`〖 〗`、`〔 〕`、`《 》`、`〈 〉`、`［ ］`、`｛ ｝`、`＜ ＞` |
| 半角符号 | `( )`、`[ ]`、`{ }`、`< >`、`" "`、`' '` |

### 能改快捷键或固定标点样式吗？

可以。从标点面板右下角或托盘右键菜单打开「快捷键设置」。23种固定符号、跟随输入法的操作和打开面板的快捷键都可以改键、清除或恢复默认。组合键须含 Ctrl 或 Alt，`Ctrl+C`、`Ctrl+V` 和 `F12` 保留。

默认根据 Windows 可读取的键盘布局和输入法状态选择中文、半角或全角标点。微软拼音单独切换中英文标点时，Windows 提供的状态可能没有同步变化。要始终用同一种样式，可以在设置中选择「固定中文标点」「固定半角标点」或「固定全角标点」。

## 支持哪些 Windows 应用？会保留原格式吗？

目前在 Windows 11 x64 的原生文本框和 Chrome 网页输入框上做过实测。标准 Windows 文本框可直接替换选区。其他应用需提供可验证的可编辑选区，并允许复制与粘贴纯文本。走剪贴板路径时，**选区内的富文本格式可能丢失**。遇到无法安全备份的剪贴板内容时，操作会停止。其他程序在操作期间写入的新剪贴板内容会保留。

密码框、只读控件、管理员权限窗口和自定义编辑器可能受限。全局快捷键也可能与前台应用的命令冲突，可为对应操作改键或清除绑定。

## 为什么做对符输入？

在 Word 等编辑器里，为已有文字补上引号、括号或书名号，经常要做几步。输入法能一次打出成对符号，但当文字已经写好时，我通常得先把光标移到两端分别补符号，或者先剪切选区、打出符号，再把文字粘贴到中间。

编辑文档、填写表单或回复消息时，都可能要给已经输入的文字补标点。我想把这几步缩成一次操作。**选中文字，按一下对应的符号快捷键，让两边的标点同时补上。**

Word 宏能解决一部分场景，[Quicker 的「超级对符」](https://www.getquicker.net/Sharedaction?code=0cf35569-18d3-4ac6-99bf-08dcff9008ea)也提供了符号配对。既然有了 AI，我还是想自己做一个，把日常最常用的操作做成一个独立的 Windows 小工具。

## 从源码构建

在 Windows 11 x64 上，从项目根目录运行 `.\Build.ps1`。脚本使用系统自带的 .NET Framework 编译器，在 `dist` 下生成安装版和便携版。

反馈问题时，可以附上 Windows 版本、目标应用、输入法和触发问题的快捷键。 [提交 Issue](https://github.com/Buds-2025/PairTap/issues/new)

**English.** PairTap is a Windows tray utility that wraps selected text in matching quotes or brackets using global keyboard shortcuts. It supports 23 punctuation pairs, including Chinese book-title marks, and provides installer and portable editions. [Download the latest release](https://github.com/Buds-2025/PairTap/releases/latest).
