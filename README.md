# TuckPane

Windows 11 x64 桌面文件收纳工具。

## 下载

从 [Releases](/https://github.com/Relawliet/TuckPane/releases/latest) 下载：

- `TuckPane-1.0.2-win-x64-setup.exe`：离线安装器，无需管理员权限，自动创建桌面快捷方式。
- `TuckPane-1.0.2-win-x64-portable.zip`：解压后双击 `00-启动 TuckPane.exe`。

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

## 许可

TuckPane 使用 [MIT](LICENSE) 许可证。第三方运行库条款见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
