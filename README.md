# TuckPane

TuckPane 是一款 Windows 11 x64 桌面文件收纳工具。它把文件和文件夹集中到可收起的桌面窗口中，需要时展开，平时尽量少占用桌面空间。

## 功能

- 创建多个相互独立的收纳窗口，并在悬浮模式和桌面定位模式之间直接切换。
- 支持拖放文件、文件夹和快捷方式；收纳内容保存在真实目录中，不锁定在应用内部。
- 右键可打开设置、复制空白窗口、切换模式、重命名、打开收纳目录或删除窗口；删除非空窗口前会把内容完整导出到桌面。
- 展开画布可从四边和四角等比例拉伸，尺寸会自动保存；画布缩小时会同步缩小放不下的内容。
- 鼠标位于展开画布内时，可用 `Ctrl + 滚轮` 调整图标、文件名和间距比例。
- 支持浅色、灰色、纯浅色和纯深色主题，以及简体中文、English、日本語。

## 下载

当前版本：**1.0.6**。也可以前往 [Latest Release](https://github.com/ch998244353/TuckPane/releases/latest) 查看完整发布说明。

- [TuckPane-1.0.6-win-x64-setup.exe](https://github.com/ch998244353/TuckPane/releases/download/v1.0.6/TuckPane-1.0.6-win-x64-setup.exe)：离线安装器，无需管理员权限，自动创建桌面快捷方式。
- [TuckPane-1.0.6-win-x64-portable.zip](https://github.com/ch998244353/TuckPane/releases/download/v1.0.6/TuckPane-1.0.6-win-x64-portable.zip)：解压后双击 `00-启动 TuckPane.exe`。
- [SHA256SUMS.txt](https://github.com/ch998244353/TuckPane/releases/download/v1.0.6/SHA256SUMS.txt)：下载文件的 SHA-256 校验值。

两种版本均自带 .NET 与 Windows App SDK，不需要另外安装运行环境。当前安装器未进行代码签名，Windows SmartScreen 可能显示“未知发布者”；可使用同一 Release 中的 `SHA256SUMS.txt` 校验文件。

系统要求：Windows 11 x64，版本 22000 或更高。

## 数据

新安装默认使用 `%USERPROFILE%\TuckPane` 和 `%LOCALAPPDATA%\TuckPane`。如果电脑上只有旧版 GlassFolder 数据，TuckPane 会原地继续使用，不复制或移动收纳文件。

卸载不会删除收纳文件或设置。便携包无需安装，但用户数据仍保存在上述用户目录。

## 构建

需要 .NET SDK 10.0.400 和 Inno Setup 6：

```powershell
.\scripts\build-release.ps1
```

核心交互逻辑检查：

```powershell
dotnet run --project .\tests\TuckPane.LogicChecks\TuckPane.LogicChecks.csproj -c Release -p:Platform=x64
```

## 许可

TuckPane 使用 [MIT](LICENSE) 许可证。第三方运行库条款见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
