# TuckPane 1.0.8

Windows 11 x64 离线发行版。

- 修复普通启动时控制台直接出现在任务栏的问题；首次启动现在只显示系统托盘图标。
- 从托盘或重复启动 TuckPane 时仍可正常打开控制台。
- 关闭控制台只会隐藏回系统托盘，退出应用仍需使用托盘菜单。

- `setup.exe`：当前用户安装，创建开始菜单和桌面快捷方式。
- `portable.zip`：解压后双击 `00-启动 TuckPane.exe`。
- 两者均自带 .NET 与 Windows App SDK。

本发行版尚未进行代码签名，Windows SmartScreen 可能显示“未知发布者”。请使用 `SHA256SUMS.txt` 校验下载文件。
