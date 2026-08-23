# TuckPane 1.0.9

Windows 11 x64 离线发行版。

- 修复展开桌面收纳窗口后，TuckPane 页面重新出现在任务栏的问题。
- 收纳窗口现在从第一次显示起就归属桌面层，展开和收起都不会创建任务栏按钮。
- 回归检查新增真实 Windows 任务栏按钮验证，覆盖收纳窗展开、重复启动打开控制台和关闭控制台回到托盘。

- `setup.exe`：当前用户安装，创建开始菜单和桌面快捷方式。
- `portable.zip`：解压后双击 `00-启动 TuckPane.exe`。
- 两者均自带 .NET 与 Windows App SDK。

本发行版尚未进行代码签名，Windows SmartScreen 可能显示“未知发布者”。请使用 `SHA256SUMS.txt` 校验下载文件。
