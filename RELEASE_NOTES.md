# TuckPane 1.0.7

Windows 11 x64 离线发行版。

- 展开收纳窗时会从 Explorer 桌面层临时抬到普通窗口最前，避免被浏览器等窗口遮挡。
- 展开窗不会持续置顶；随后切换到其他窗口时，其他窗口仍可正常覆盖它。
- 收缩动画完成后会重新回到桌面层，维持桌面组件原有的层级行为。

- `setup.exe`：当前用户安装，创建开始菜单和桌面快捷方式。
- `portable.zip`：解压后双击 `00-启动 TuckPane.exe`。
- 两者均自带 .NET 与 Windows App SDK。

本发行版尚未进行代码签名，Windows SmartScreen 可能显示“未知发布者”。请使用 `SHA256SUMS.txt` 校验下载文件。
