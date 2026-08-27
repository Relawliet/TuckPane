# TuckPane 2.0.2

## English

TuckPane 2.0.2 is a focused visual and window-behavior update.

- Expanded Stations continue to stay above ordinary applications without taking keyboard focus, and leave the topmost layer again after collapsing.
- Image files now use Windows native thumbnails with their original aspect ratio; unsupported or failed previews still fall back to the existing Shell icon.
- Frosted Light and Frosted Dark now match their high-opacity previews. The Frosted Light organizer surface uses a calmer `#E2E5E9` tone while the settings window keeps its original light appearance.
- Note windows keep native resizing and shadows while suppressing the visible dark DWM border.

### Downloads

- `TuckPane-2.0.2-win-x64-setup.exe`: per-user offline installer with Start menu and desktop shortcuts plus `.tucknote` file association.
- `TuckPane-2.0.2-win-x64-portable.zip`: extract it and run `00-启动 TuckPane.exe`; it does not register file associations.
- `SHA256SUMS.txt`: SHA-256 checksums for both packages.

Both packages include .NET and the Windows App SDK. No separate runtime is required.

## 简体中文

TuckPane 2.0.2 是一次聚焦视觉与窗口行为的更新。

- 中转站继续在展开时保持于普通应用上方且不抢键盘焦点，并在收缩后解除置顶。
- 图片文件改用 Windows 原生缩略图并保留原始宽高比；无法生成预览时继续回退现有 Shell 图标。
- 白色磨砂与深色磨砂现在和高不透明预览一致；白色磨砂收纳窗改用更柔和的 `#E2E5E9`，设置窗口仍保持原有浅色外观。
- 便签继续保留原生拖边缩放和系统阴影，同时去除可见的深色 DWM 描边。

### 下载

- `TuckPane-2.0.2-win-x64-setup.exe`：当前用户离线安装器，创建开始菜单和桌面快捷方式，并注册 `.tucknote` 文件关联。
- `TuckPane-2.0.2-win-x64-portable.zip`：解压后运行 `00-启动 TuckPane.exe`；便携版不会主动注册文件关联。
- `SHA256SUMS.txt`：两个安装包的 SHA-256 校验值。

两种版本都已包含 .NET 与 Windows App SDK，不需要另行安装运行环境。

本版本未进行代码签名，Windows SmartScreen 可能显示“未知发布者”；需要时请使用 `SHA256SUMS.txt` 校验下载文件。

This release is unsigned. Windows SmartScreen may show an “Unknown publisher” warning; verify downloads with `SHA256SUMS.txt` when needed.
