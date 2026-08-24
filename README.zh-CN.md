# TuckPane

[English](README.md) | [简体中文](README.zh-CN.md)

TuckPane 是一款 Windows 11 x64 桌面文件收纳工具。它把真实文件和文件夹集中到可收起的桌面窗口中，需要时展开，平时尽量少占用桌面空间。

## 动态演示

<p align="center">
  <img src="docs/images/demo-expand-collapse.gif" alt="TuckPane 收纳窗展开和收缩演示" width="420">
  <img src="docs/images/demo-file-reorder.gif" alt="在 TuckPane 收纳窗内调整文件位置" width="420">
  <br><sub>左：展开和收缩收纳窗。右：拖动文件，在收纳窗内调整位置。</sub>
</p>

## 界面展示

<p align="center">
  <img src="docs/images/organizer-expanded.png" alt="展开后的 TuckPane 收纳窗，其中显示文件和文件夹" width="720">
  <br><sub>需要查看内容时再展开收纳窗。</sub>
</p>

<p align="center">
  <img src="docs/images/context-menu.png" alt="包含收纳窗快捷操作的 TuckPane 右键菜单" width="344">
  <br><sub>右键即可进入设置、复制窗口、切换模式、重命名、打开保存目录或安全删除。</sub>
</p>

<p align="center">
  <img src="docs/images/manage-settings.png" alt="TuckPane 收纳窗管理设置" width="900">
  <br><sub>分别调整每个收纳窗的网格、模式、主题、入口大小、画布大小和内容比例。</sub>
</p>

<p align="center">
  <img src="docs/images/themes.png" alt="TuckPane 浅色毛玻璃、深色毛玻璃、纯浅色和纯深色主题" width="800">
  <br><sub>可选浅色毛玻璃、深色毛玻璃、纯浅色和纯深色主题。</sub>
</p>

### 快捷功能

可以直接把文件和文件夹拖入收纳窗，通过右键菜单管理窗口，按住 `Ctrl` 并滚动鼠标调整内容大小，选择点击窗口外自动收缩，并让 TuckPane 安静驻留在系统托盘中。

## 功能

- 最多创建 12 个相互独立的收纳窗口，并在悬浮模式和桌面定位模式之间切换。
- 支持拖入文件、文件夹、应用快捷方式和 Steam `.url` 快捷方式；Windows 提供原始图标时会正常保留。
- 展开画布可从四边和四角等比例拉伸，尺寸和内容布局会自动保存。
- 鼠标位于展开画布内时，可用 `Ctrl + 滚轮` 调整图标、文件名和间距比例。
- 可选择在点击窗口外时自动收缩；这次点击仍会传递给桌面或目标程序。
- 右键可打开设置、复制空白窗口、切换模式、重命名、打开收纳目录或删除窗口。
- 支持浅色、灰色、纯浅色和纯深色主题，以及 English、简体中文、日本語界面。
- 启动后只驻留系统托盘。关闭设置窗口只会隐藏；只有托盘菜单中的“退出”会结束 TuckPane。

## 下载

当前版本：**1.0.11**。完整发布说明请查看 [Latest Release](https://github.com/ch998244353/TuckPane/releases/latest)。

- [TuckPane-1.0.11-win-x64-setup.exe](https://github.com/ch998244353/TuckPane/releases/download/v1.0.11/TuckPane-1.0.11-win-x64-setup.exe)：当前用户离线安装器，创建开始菜单和桌面快捷方式。
- [TuckPane-1.0.11-win-x64-portable.zip](https://github.com/ch998244353/TuckPane/releases/download/v1.0.11/TuckPane-1.0.11-win-x64-portable.zip)：解压后双击 `00-启动 TuckPane.exe`。
- [SHA256SUMS.txt](https://github.com/ch998244353/TuckPane/releases/download/v1.0.11/SHA256SUMS.txt)：两个下载文件的 SHA-256 校验值。

两种版本均自带 .NET 与 Windows App SDK，不需要另行安装运行环境。当前安装器未进行代码签名，Windows SmartScreen 可能显示“未知发布者”；需要时可使用 `SHA256SUMS.txt` 校验下载文件。

系统要求：Windows 11 x64，版本 22000 或更高。

## 存储与数据

新安装默认把收纳数据保存到 `%USERPROFILE%\TuckPane`，把设置和缓存保存到 `%LOCALAPPDATA%\TuckPane`。如果电脑上只有旧版 GlassFolder 数据，TuckPane 会原地继续使用，不复制或移动已有收纳文件。

每个新收纳窗使用一个类似 `%USERPROFILE%\TuckPane\Windows\名称-ID` 的单层目录，文件直接保存在其中。也可以选择一个已有的专用目录作为最终保存位置，该目录现有的顶层内容会立即显示。为了避免误操作无关数据，TuckPane 会拒绝范围过大或与其他收纳目录重叠的位置。

删除非空收纳窗前，TuckPane 会把整个保存目录导出到桌面上的唯一目录；导出失败或取消时，源目录和窗口状态都会保留。卸载 TuckPane 不会删除收纳文件或设置。

## 构建

安装 .NET SDK 10.0.400 和 Inno Setup 6，然后运行：

```powershell
.\scripts\build-release.ps1
```

运行精简的核心逻辑回归检查：

```powershell
dotnet run --project .\tests\TuckPane.LogicChecks\TuckPane.LogicChecks.csproj -c Release -p:Platform=x64
```

## 许可

TuckPane 使用 [MIT](LICENSE) 许可证。第三方运行库条款见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
