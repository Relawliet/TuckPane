# TuckPane 项目分析

本文档描述当前源码工作树的运行架构。它是后续修改启动、窗口所有权、状态、存储和拖放行为时的项目级入口；具体实现仍以当前源码和 CodeGraph 为准。

## 1. 本机容器、源码与安装目录

本机把源码和运行版收拢在同一个容器中，但二者仍是不同边界：

```text
D:\app\功能\TuckPane\
├─ source\          Git 源码根目录
├─ app\current\     当前自包含安装版及卸载器
├─ AGENTS.md         指向 source\AGENTS.md
├─ .agents\          指向 source\.agents
└─ .codegraph\       指向 source\.codegraph
```

- 开发、测试和发布只从 `source` 进行；`app/current` 只接收安装器部署结果，禁止作为源码修改位置。
- 独立克隆仓库时，克隆目录本身就是源码根，不要求存在外层容器或 `app/current`。
- `.agents` 是源码树中的项目级 WinUI Agent 工具；根目录链接只用于本机发现，不复制第二份内容。
- 正常用户真实文件位于 `C:\Users\ch\GlassFolder`，状态、日志和图标缓存位于 `C:\Users\ch\AppData\Local\GlassFolder`；它们不属于源码、安装输出或清理范围。
- 所有自动化测试都应通过 `TUCKPANE_TEST_ROOT` 隔离状态、存储、日志和桌面目录。

## 2. 启动与所有权

```text
Program.Main
        ├─ AppInstance.FindOrRegisterForKey
        │       └─ 第二进程激活重定向（普通启动 / --startup / .tucknote）
        ▼
App.OnLaunched
        │
        ▼
AppHost.InitializeAsync
        ├─ 单实例保护、状态加载、语言和开机启动设置
        ├─ ConsoleWindow（隐藏宿主 HWND、托盘入口）
        ├─ TransferQueue（跨窗口文件传输串行化与取消）
        ├─ MainWindow × N（每个 OrganizerDefinition 一个）
        │       └─ StorageService（该窗口真实收纳目录）
        ├─ NoteWindow × 已打开内部或外部便签（关闭只隐藏，不随收纳窗折叠）
        └─ NoteStore（内部正文及 .tucknote 严格读写）
```

`AppHost` 是应用级协调者：持有状态、托盘/控制窗口、所有收纳窗口、已打开的内部/外部便签窗口和传输队列，并负责创建、复制、删除、显示及退出。`MainWindow` 只负责一个收纳窗口的界面与交互；它通过宿主回调保存状态或请求跨窗口操作，不拥有整个应用生命周期。`NoteWindow` 是置顶且不进入任务栏/Alt+Tab 的独立工具窗；深色标题区由 WinUI 自定义标题栏的原生 Caption 区负责系统拖动，标题文字区域单独作为输入直通区，双击或按 F2 后切换为内联改名框，按钮和输入框不参与拖动。右上角关闭和 Alt+F4 只保存并隐藏，重新点击内部便签图标或再次打开同一路径的外部便签会复用原窗口。托盘“隐藏全部/显示全部”会同时处理两类便签，普通收纳窗折叠不影响便签。

Windows App SDK `AppInstance` 在创建 XAML 窗口前完成当前版本的实例注册和激活重定向；`TUCKPANE_TEST_ROOT` 会参与实例键，避免隔离测试撞上正常实例。重定向的 Launch 参数先通过 Windows `CommandLineToArgvW` 规则拆分；仅当首项等于当前 `Environment.ProcessPath` 时才丢弃它，因此带中文、空格和引号的 `.tucknote` 路径会作为独立参数交给主实例。普通二次启动打开总控台，`--startup` 保持托盘启动。原有 `SingleInstanceGuard` mutex/event 继续作为旧版本兼容保护。

正常退出从托盘命令进入 `AppHost.ExitAsync`：先停止继续接收交互，保存状态，关闭各窗口和控制窗口，再释放单实例资源。不要用强制结束进程代替正常退出，除非测试清理已确认使用隔离状态。

## 3. 三种窗口模式

`OrganizerDefinition.PlacementMode` 有三种值，但都由同一个 `MainWindow` 实现：

- `Floating`：可自由摆放的悬浮收纳窗。
- `Positioned`：保存显示器与位置的定位收纳窗。
- `Station`：贴靠屏幕边缘、由热区展开的中转站；边由 `DockEdge` 指定。

三种模式的差异集中在窗口放置、折叠/展开和可见性状态机。文件目录、项目刷新、拖入、拖出、排序和跨窗口传输共用同一套实现。因此拖放缺陷应优先修复共享链路，而不是分别在三种模式增加分支。

`GlobalSettings.ExclusiveExpansion` 默认开启并统一约束三种模式：展开新窗口时折叠上一个窗口；关闭后允许多个窗口持续展开；重新开启时立即保留最近操作的窗口并折叠其余窗口。跨窗口 Shell 拖动期间，正在导出的源窗暂不参与折叠，拖动结束后再恢复互斥。

普通 `Floating/Positioned` 窗口的悬浮展开不仅检查矩形范围，还用 `WindowFromPoint` 确认鼠标实际命中当前 HWND、子窗口或画布缩放边窗；被其他应用覆盖时不得展开。全局 `CollapseOnPointerLeave` 仅控制普通窗口：开启后复用 50ms 指针轮询，鼠标离开 400ms 才收起，重新进入立即取消。菜单、对话框、传输、窗口拖动/缩放、项目换序和系统拖放会清零计时，交互结束后重新等待完整 400ms。Station 的呼出热区覆盖其保存显示器对应方向的整条 4px 物理屏幕边缘，包括全屏应用占用但系统工作区排除的任务栏保留区域，不受展开窗口尺寸或沿边位置限制；其固定 400ms 离开收缩不受该设置影响。

Station 展开时先完成最终窗口边界与显示，再由 `DesktopLayerService.SetExpanded(..., stayTopmost: true)` 脱离桌面 owner 并应用 `WS_EX_TOPMOST`；`WS_EX_NOACTIVATE` 保持不变，因此覆盖普通同完整性级别应用但不抢键盘焦点。收缩完成后 `SetExpanded(false)` 先解除 topmost，再隐藏并恢复折叠状态。普通 `Floating/Positioned` 窗口仍只做一次非持续置顶的抬升，不改变其层级契约。

## 4. 状态与持久化

- 根状态模型是 `AppStateV2`，当前 Schema 为 5；包含全局设置和 `OrganizerDefinition` 列表。
- `GlobalSettings.CollapseOnPointerLeave` 缺失时按 `false` 处理，不提升 Schema；总控台“设置 → 通用”负责保存并在失败时回滚开关。
- `GlobalSettings.ExclusiveExpansion` 缺失时通过属性初始化器按 `true` 处理，不提升 Schema；语言字段缺失或无效时回退中文，显式保存的中文、英文和日文保持不变。
- `StateStore.LoadAsync` 负责读取、迁移和规范化旧状态；无效的窗口数量、网格、位置和模式组合会在这里收敛。
- `StateStore.SaveAsync` 通过临时文件和备份文件完成替换，避免进程中断时直接损坏主状态文件。
- 每个收纳窗保存身份、名称、模式、布局、显示器/位置、缩放、主题、目录映射、项目顺序和独立的 `Notes` 列表。便签定义保存名称、七色主题、全局字号、正文横线开关和工具窗几何；`ShowRuledLines` 缺失时按 `false` 处理，不提升 Schema。正文及内嵌图片不进入状态 JSON。
- `AppPaths` 决定正常用户根目录以及 `TUCKPANE_TEST_ROOT` 隔离根目录。`note-staging` 也位于相同本地根；启动仅清理其直接 GUID 暂存子目录。测试不得读写正常用户状态。

## 5. 真实目录、刷新与传输

每个收纳窗对应一个真实文件系统目录。`AppPaths.ResolveStoragePath` 解析窗口的相对或绝对存储位置，`StorageService` 负责枚举、重名处理、导入、复制、导出、快捷方式和目录创建。

便签正文由 `NoteStore` 存在本地状态根的 `notes` 子目录，采用临时文件、主文件和备份文件替换；粘贴图片经过编辑器白名单清理后以内嵌 data URI 保存，因此不会出现在收纳窗的真实文件目录。复制收纳窗时每个便签取得新 ID 并复制正文；删除收纳窗时先成功导出真实目录，再删除所属便签正文。

可移植便签是无 BOM UTF-8 JSON `.tucknote` v1，固定字段为 `format="TuckPane.Note"`、`version=1`、`theme`、`fontSize`、`showRuledLines`、`placement`、`html`；文件名就是窗口标题，不保存 organizer/note ID。读取边界为 64 MiB，并严格拒绝缺失/未知字段、损坏 JSON、未知版本/主题、非法字号或几何。外部便签以打开路径为唯一数据源，保存只通过同目录临时文件原子替换；源文件被移动或删除后只报错，不在旧路径重建。外部便签内联改名会先保存正文，再校验空名、非法/保留文件名和目标冲突，通过同目录 `File.Move` 改真实文件名，并同步打开窗口路径索引、托盘隐藏集合和所属收纳窗 `ItemOrder`；状态保存失败时回滚这些运行时索引和文件名。

文件操作结束后，窗口重新读取目录，并把真实文件与 `Notes` 生成的虚拟图标合并后按 `ItemOrder` 恢复用户顺序；不存在的顺序项会被清理，新项目会进入可见列表。`.tucknote` 在枚举时只获得运行时 `PortableNote` 分类：不写入状态、不改变 Schema，但会隐藏扩展名、使用与内部便签相同的专用图标并支持单击打开；拖出时仍是普通真实文件。应用级 `TransferQueue` 让移动/复制任务按顺序执行并支持取消，避免多个窗口同时改动相同文件时产生竞态。失败通过 `TransferOutcome` 返回，界面只报告真实失败，不把未完成操作写成成功状态。

## 6. 拖入数据流

```text
外部 OLE 拖入
  → MainWindow.WindowRoot / ItemsGrid DragOver、Drop
  → 按来源 AllowedOperations 选择 Move，或在 Copy-only 时选择 Copy
  → 读取 StandardDataFormats.StorageItems
  → 提取真实文件或文件夹路径
  → TransferQueue
  → Move: StorageService.ImportBatchAsync
  → Copy: StorageService.CopyBatchAsync
  → 刷新目录与顺序
```

接收端沿用 Windows 的 StorageItems/FileDrop 数据，并按来源允许的操作协商：来源支持 Move 时保持 Explorer 的物理移动；不支持 Move 但支持 Copy 时执行复制，覆盖 Edge/Chrome 已完成下载项的 Copy/Link 数据对象；只有 Link 或没有有效本地路径时拒绝。拖回来的 `.tucknote` 与其他真实文件一样导入收纳目录，不转换成内部虚拟便签；在 TuckPane 网格中单击即由当前主实例打开，在资源管理器或外部应用中仍按正常文件关联处理。文件夹按真实目录处理，不自动压缩。

## 7. 拖出数据流

所有模式共用下面的源链路：

```text
MainWindow 项目指针拖动
  → 先进入窗口内重排状态
  → 鼠标仍在完整窗口边界内：更新并提交 ItemOrder
  → 鼠标离开完整窗口边界：边界钩子投递外拖升级
  → 普通文件/文件夹/便签：BeginXamlShellDrag → UIElement.StartDragAsync
  → .lnk/.url：ShellDragService.DoDragDrop 原生 Shell IDataObject
  → DataPackage.SetStorageItems(普通真实文件/文件夹，或暂存 .tucknote)
  → 普通项目：Copy | Move | Link；便签：Copy | Move，默认 Move
  → 根据目标返回效果分类并收尾
```

真实界面在鼠标越过完整窗口边界后才升级为系统拖放。普通文件和文件夹继续把 `StorageFile/StorageFolder` 放入 WinUI `DataPackage`；WinUI 将其桥接为标准系统文件拖放数据。由于 Windows 会拒绝 WinRT 为部分 `.lnk/.url` 路径创建 `StorageFile`，这两类快捷方式改为复用现有 `ShellDragService`，直接从原始路径创建 Shell `IDataObject`，拖出的是快捷方式文件本身，不解析目标。两条路径最终都向标准接收端提供 `FileDrop/CF_HDROP` 绝对路径，由目标、源/目标卷和修饰键协商 Copy、Move 或 Link。

便签越界外拖前先强制保存并暂时隐藏已打开的窗口，再在隔离的 `note-staging/<GUID>` 下生成真实 `.tucknote` 并复用同一 `StartDragAsync/SetStorageItems` 链路。目标返回 Move 后才删除内部 `NoteDefinition`、`ItemOrder` 项及正文；Copy、取消、拒绝或异常均保留内部便签、恢复原窗口并清理暂存文件。便签不允许 Link。

低级钩子只负责判断是否越过窗口边界并把升级请求送回 UI 线程，不直接承担 OLE 消息循环。普通项目继续依赖 WinUI/OLE 桥接保持输入连续性；只有 WinRT 无法实体化的 `.lnk/.url` 使用原生 Shell 数据对象。拖动期间 `AppHost` 暂缓折叠活动源窗，结束后调用共享互斥收敛逻辑，成功转移时保留目标窗，取消时源窗仍可继续交互。

结果处理边界：

- 窗口内部排序：普通项目以 Link、便签以 Move 作为内部协商结果；`MainWindow` 只更新 `ItemOrder`，不删除源。
- TuckPane 窗口之间：目标通过现有传输路径移动真实文件，源窗与目标窗随后刷新。
- 桌面与资源管理器：由系统文件拖放目标直接完成移动/复制及桌面落点处理；目录监视器随后刷新源窗。
- 外部 Copy：保留收纳目录中的源项目。
- 外部 Move：目标完成移动后刷新收纳目录。
- 外部 Link：保留源项目并刷新界面状态。
- 目标取消或不接受：不改动源文件。高权限目标拒绝普通权限拖入属于 Windows UIPI 边界。
- 便签外部 Move：目标完成接收后删除内部便签；便签 Copy/取消：保留内部便签并恢复窗口。

真实文件系统项目的应用内右键菜单固定为“剪切、删除”，文件、文件夹、`.lnk`、`.url` 和外部 `.tucknote` 共用同一路径；内部虚拟便签仍保留“重命名、删除”。剪切通过 `ShellDragService` 向系统剪贴板写入 `CF_HDROP` 和 `Preferred DropEffect=Move`，Explorer 可按标准移动语义粘贴；TuckPane 粘贴在 WinRT `StorageItems` 无法实体化快捷方式时回退读取原生 `CF_HDROP`。删除调用 .NET 提供的 Windows 回收站操作，不显示应用确认框，失败时保留源文件并报告错误。旧的完整 Shell 菜单辅助进程已移除。

## 8. 发布与本机安装

`scripts/build-release.ps1` 先生成 Release x64 自包含发布目录，再在打包前创建与 `TuckPane.exe` 哈希一致的 `00-启动 TuckPane.exe`，最后由 Inno Setup 生成离线安装器和便携包。本机安装器使用自定义目录 `app/current`；公开安装器的默认目录仍由 Inno Setup 决定，不把本机绝对路径写进源码。

Inno 安装器在当前用户 `HKCU\Software\Classes` 注册 `.tucknote` 的 `TuckPane.Note` ProgID、应用图标和 `"TuckPane.exe" "%1"` 打开命令，并在卸载时清理自身关联。便携包不主动修改注册表，但相同可执行文件仍接受命令行/“打开方式”传入的 `.tucknote`。

迁移或覆盖安装时必须先从托盘正常退出用户实例。安装完成后从真实安装路径启动，`StartupService.Apply` 会按 `Environment.ProcessPath` 刷新开机启动项；桌面、开始菜单和卸载注册也必须指向 `app/current`。发布文件与安装文件需要逐文件 SHA-256 比对，卸载器只能管理安装目录，不能覆盖或删除 `source`。

## 9. 失败处理与测试入口

- Shell/COM 失败在共享拖放服务边界转为明确结果或异常；文件传输以逐项结果报告，避免半成功被整体吞掉。
- `TuckPane.LogicChecks` 是无测试框架的核心回归入口。
- `TuckPane.LogicChecks --aug26-fixes` 只验证中文默认/显式语言保留、互斥默认值、Move/Copy 操作选择和新增主题枚举/分类。
- `--external-file-drop` 启动跨进程 OLE 探针；隐藏子进程参数 `--external-file-drop-target <Copy|Move|Link>` 只供该探针使用。接收端读取 `FileDrop/CF_HDROP`，验证真实路径和协商结果。
- `tests/ExternalMainWindowDrop.ps1` 驱动真实 `MainWindow`，覆盖文件与快捷方式窗口内换序，以及文件、文件夹、`.lnk`、`.url` 的标准 FileDrop。任务级 UI 用例只验证真实文件菜单严格包含“剪切、删除”，不再启动或断言完整 Shell 菜单辅助进程。
- `tests/NoteFeatureCheck.ps1` 在独立 `TUCKPANE_TEST_ROOT` 中验证便签功能；定向入口 `-TitleDragOnly` 覆盖深色标题拖动、按钮隔离、内部内联改名和 Esc，`-PortableNoteOnly` 覆盖 `.tucknote` 跨收纳窗真实移动、单击打开、文件/顺序同步以及重名、保留名和 Esc，`-ActivationOnly` 覆盖带中文空格路径的第二实例重定向，`-RuledLinesOnly` 生成默认/大字号中英文下沉字形截图并核对横线状态。无开关时保留原有空白新建、文字粘贴、图片、颜色、关闭隐藏、传统菜单改名与删除回归。
- `tests/WindowLayerCheck.ps1` 除窗口层级外，还验证普通窗口被覆盖时不悬浮展开、重新暴露后恢复，以及 400ms 离开收缩和提前返回取消；定向入口 `-StationCoveredOnly` 只在 `TUCKPANE_TEST_ROOT` 中验证主屏右边缘 Station 的展开、desktop owner 脱离、持续 topmost、no-activate、不抢焦点、覆盖普通窗口以及隐藏后解除 topmost。
- 托盘启动回归使用 `scripts/check-tray-startup.ps1`；运行前必须确认没有正常 TuckPane 实例。

常用验证命令：

```powershell
dotnet run --project .\tests\TuckPane.LogicChecks\TuckPane.LogicChecks.csproj -c Release -p:Platform=x64
dotnet run --project .\tests\TuckPane.LogicChecks\TuckPane.LogicChecks.csproj -c Release -p:Platform=x64 -- --external-file-drop
& .\tests\WindowLayerCheck.ps1 -ExePath '<TuckPane.exe>' -StationCoveredOnly
& 'D:\app\功能\TuckPane\.agents\skills\winui-dev-workflow\BuildAndRun.ps1' '.\src\TuckPane\TuckPane.csproj' -SkipRun /p:Configuration=Release
```

## 10. 维护规则

修改前先读本文档并查询当前 CodeGraph。若启动/所有权、窗口模式、状态 Schema、存储目录、传输队列、拖放或发布/安装边界发生变化，必须在同一任务内同步更新本文档，并把逻辑检查、跨进程探针、WinUI 构建、真实 UI 和安装版验收分别报告，不能用其中一项代替另一项。
