# TuckPane 2.0.0

## English

TuckPane 2.0 adds edge Stations, integrated notes, and a more complete Windows file workflow.

- Added **Station mode** for left, top, right, or bottom monitor edges. Stations use the full physical edge as their reveal zone, size themselves to their content, support multi-display placement, remain above ordinary applications while expanded, and do not take keyboard focus.
- Added hover-to-expand, pointer-leave collapse, and exclusive-expansion settings for ordinary panes. Expansion behavior now accounts for overlapping applications and active drag interactions.
- Added integrated rich notes with pasted images, seven themes, adjustable text size, optional ruled lines, inline title renaming, saved placement, tray visibility handling, and atomic local persistence.
- Added portable `.tucknote` v1 files. Notes can be dragged out, copied or moved between panes, opened through a second app activation, renamed as real files, and associated with TuckPane by the installer.
- Expanded file operations with paste, new-folder creation, Windows clipboard cut, Recycle Bin deletion, and Copy/Move negotiation when importing from applications that cannot offer Move.
- Improved shell drag-and-drop for files, folders, `.lnk`, `.url`, and `.tucknote` items across TuckPane panes, Explorer, the desktop, and other standard Windows targets.
- Improved single-instance activation for quoted paths containing spaces or non-ASCII characters, and fixed the main-screen Station being left behind a covering application after expansion.
- Updated English, Simplified Chinese, and Japanese resources for the new Station, note, and interaction settings.

### Downloads

- `TuckPane-2.0.0-win-x64-setup.exe`: per-user offline installer with Start menu and desktop shortcuts plus `.tucknote` file association.
- `TuckPane-2.0.0-win-x64-portable.zip`: extract it and run `00-启动 TuckPane.exe`; it does not register file associations.
- `SHA256SUMS.txt`: SHA-256 checksums for both packages.

Both packages include .NET and the Windows App SDK. No separate runtime is required.

## 简体中文

TuckPane 2.0 新增屏幕边缘中转站、内置便签和更完整的 Windows 文件操作流程。

- 新增左、上、右、下四个方向的**中转站模式**。中转站使用显示器整条物理边缘作为呼出热区，可按内容自适应尺寸并选择显示器；展开后位于普通应用上方，同时不抢占键盘焦点。
- 普通收纳窗新增悬浮后展开、鼠标离开后收缩和只允许单窗展开设置；窗口被其他应用覆盖或正在拖放时会遵守对应交互边界。
- 新增内置富文本便签，支持粘贴图片、七种主题、字号调整、正文横线、标题内联改名、窗口位置保存、托盘显隐和原子化本地存储。
- 新增便携 `.tucknote` v1 文件。便签可拖出、在收纳窗之间复制或移动、通过第二次启动打开，并像真实文件一样改名；安装版会注册文件关联。
- 文件操作新增粘贴、新建文件夹、Windows 剪贴板剪切和移入回收站；从不支持 Move 的应用拖入时可按来源能力执行 Copy。
- 改进文件、文件夹、`.lnk`、`.url` 和 `.tucknote` 在 TuckPane、资源管理器、桌面及其他标准 Windows 目标之间的拖放。
- 改进包含空格、中文或引号路径的单实例激活，并修复主屏中转站展开后仍可能位于覆盖应用后方的问题。
- 为中转站、便签和交互设置补齐 English、简体中文和日本語资源。

### 下载

- `TuckPane-2.0.0-win-x64-setup.exe`：当前用户离线安装器，创建开始菜单和桌面快捷方式，并注册 `.tucknote` 文件关联。
- `TuckPane-2.0.0-win-x64-portable.zip`：解压后运行 `00-启动 TuckPane.exe`；便携版不会主动注册文件关联。
- `SHA256SUMS.txt`：两个安装包的 SHA-256 校验值。

两种版本都已包含 .NET 与 Windows App SDK，不需要另行安装运行环境。

This release is unsigned. Windows SmartScreen may show an “Unknown publisher” warning; verify downloads with `SHA256SUMS.txt` when needed.
