---
version: alpha
name: PairTap
description: Windows 11 标点选择面板
colors:
  canvas: "#F5F6F8"
  tile: "#FFFFFF"
  ink: "#1A2333"
  muted: "#576477"
  primary: "#165EC5"
  outline: "#D9DFE8"
typography:
  interface:
    fontFamily: Microsoft YaHei UI
  symbol:
    fontFamily: Segoe UI
rounded:
  tile: 8px
spacing:
  tileGap: 8px
  contentInset: 24px
omitted:
  - section: components
    reason: 控件状态直接维护在 Palette.cs
---

## Overview

面板按引号、中文括号与书名号、半角符号组织。每个按钮用两个蓝色标点夹住灰色的「文」，可以在看标签前识别包裹效果。标题区的应用标记由矢量路径绘制。

## Colors

浅色使用冷灰底、白色符号区和蓝色标点。深色跟随 Windows 应用颜色设置。开启高对比度时，使用 Windows 系统色。运行时颜色统一由 Palette.cs 持有。

## Typography

中文标题和说明使用 Microsoft YaHei UI。符号和功能键标注使用 Segoe UI，并由系统提供缺字回退。

## Layout

符号网格随窗口宽度重排。高度不足时只滚动符号区。快捷键在标签下方占用独立区域，长组合键换行显示。底栏固定，左侧显示操作提示，右侧进入快捷键设置。窗口使用系统边框缩放和最大化。

## Components

按钮保留原生 Tab、Enter 和辅助功能语义。悬停和按压改变底色，键盘焦点由加粗描边标识。选择完成后，面板关闭并返回原输入焦点。
