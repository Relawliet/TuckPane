# TuckPane 1.0.11

## English

- English is now the default interface language. Existing v1/v2 settings migrate to English once; later language choices remain saved.
- Added an optional **Collapse when clicking outside** setting. It is off by default, and the outside click still reaches its target.
- New panes store files directly in a single `Name-ID` directory. A manually selected directory is now used as the final storage location, including its existing top-level contents.
- Added safety checks for broad, network, and overlapping storage locations. Deleting a pane backed by a selected directory exports that entire directory before removal.
- Fixed Steam `.url` shortcuts showing a generic blank document instead of their declared game icon.
- Added a complete English README and a matching Simplified Chinese README.

## 简体中文

- 默认界面语言改为 English。v1/v2 设置会一次性迁移到英文，之后用户重新选择的语言会继续保存。
- “通用”新增“点击窗口外自动收缩”开关，默认关闭；触发收缩的点击仍会传递给目标位置。
- 新建收纳窗直接使用单层 `名称-ID` 目录；手选目录会直接作为最终保存位置，并立即显示已有顶层内容。
- 增加对宽泛目录、网络目录及目录重叠的安全检查；删除使用手选目录的窗口前会导出整个目录。
- 修复 Steam `.url` 快捷方式显示通用白纸图标的问题。
- 增加完整英文 README 和对应的简体中文 README。

`setup.exe` is a per-user offline installer. Extract `portable.zip` and run `00-启动 TuckPane.exe`. Both packages include .NET and the Windows App SDK.

This release is unsigned. Windows SmartScreen may show an “Unknown publisher” warning; use `SHA256SUMS.txt` to verify the downloads.
