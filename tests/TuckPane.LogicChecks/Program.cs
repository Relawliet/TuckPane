using TuckPane.Core;
using TuckPane.Models;
using TuckPane.Services;
using Windows.ApplicationModel.DataTransfer;

if (args is ["--external-file-drop"])
{
    await ExternalFileDropProbe.RunAsync();
    return;
}
if (args is ["--external-file-drop-target", var effect])
{
    ExternalFileDropProbe.RunTarget(effect);
    return;
}
if (args is ["--aug26-fixes"])
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    string root = Path.Combine(Path.GetTempPath(), $"TuckPane-aug26-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        Require(new GlobalSettings().Language == AppLanguage.ChineseSimplified,
            "新配置没有默认使用中文。");
        var exclusiveProperty = typeof(GlobalSettings).GetProperty("ExclusiveExpansion");
        Require(exclusiveProperty is not null && exclusiveProperty.GetValue(new GlobalSettings()) is true,
            "全局单窗展开开关不存在或没有默认开启。");

        string missingLanguagePath = Path.Combine(root, "missing-language.json");
        await File.WriteAllTextAsync(missingLanguagePath,
            """{"SchemaVersion":5,"GlobalSettings":{"Theme":0},"Organizers":[]}""");
        AppStateV2 missingLanguage = await new StateStore(missingLanguagePath).LoadAsync();
        Require(missingLanguage.GlobalSettings.Language == AppLanguage.ChineseSimplified,
            "缺失语言字段没有回退中文。");

        AppStateV2 explicitEnglish = StateStore.Normalize(new AppStateV2
        {
            GlobalSettings = new GlobalSettings { Language = AppLanguage.English }
        });
        AppStateV2 explicitJapanese = StateStore.Normalize(new AppStateV2
        {
            GlobalSettings = new GlobalSettings { Language = AppLanguage.Japanese }
        });
        Require(explicitEnglish.GlobalSettings.Language == AppLanguage.English &&
                explicitJapanese.GlobalSettings.Language == AppLanguage.Japanese,
            "显式保存的英文或日文被默认语言覆盖。");
        Require(StateStore.Normalize(new AppStateV2
            {
                GlobalSettings = new GlobalSettings { Language = (AppLanguage)999 }
            }).GlobalSettings.Language == AppLanguage.ChineseSimplified,
            "无效语言值没有回退中文。");

        GlassTheme frostedLight = (GlassTheme)4;
        GlassTheme frostedDark = (GlassTheme)5;
        Require(frostedLight.ToString() == "FrostedLight" && frostedDark.ToString() == "FrostedDark",
            "高不透明白色/深色磨砂主题枚举未按尾部值追加。");
        Require(!GlassThemePalette.IsDark(frostedLight) && GlassThemePalette.IsDark(frostedDark),
            "新增磨砂主题的深浅色分类错误。");
        Windows.UI.Color lightFallback = GlassThemePalette.SurfaceColor(frostedLight);
        Windows.UI.Color darkFallback = GlassThemePalette.SurfaceColor(frostedDark);
        Require(lightFallback is { R: 245, G: 245, B: 243 } &&
                darkFallback is { R: 32, G: 33, B: 36 },
            "新增磨砂主题的透明效果关闭回退色错误。");

        var selector = typeof(OrganizerInteractionMath).GetMethod(
            "SelectDropOperation",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Require(selector is not null, "拖入操作选择器不存在。");
        var select = (DataPackageOperation operation) =>
            (DataPackageOperation)selector!.Invoke(null, [operation])!;
        Require(select(DataPackageOperation.Move | DataPackageOperation.Copy) == DataPackageOperation.Move,
            "同时支持移动和复制时没有优先移动。");
        Require(select(DataPackageOperation.Copy | DataPackageOperation.Link) == DataPackageOperation.Copy,
            "浏览器 Copy/Link 来源没有选择复制。");
        Require(select(DataPackageOperation.Link) == DataPackageOperation.None,
            "仅支持链接的来源不应被接收。");

        Console.WriteLine("PASS: aug26 focused fixes");
    }
    finally
    {
        try { Directory.Delete(root, recursive: true); }
        catch { }
    }
    return;
}
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string logicRoot = Path.Combine(Path.GetTempPath(), $"TuckPane-logic-{Guid.NewGuid():N}");
Environment.SetEnvironmentVariable("TUCKPANE_TEST_ROOT", logicRoot);
Directory.CreateDirectory(logicRoot);
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    try { if (Directory.Exists(logicRoot)) Directory.Delete(logicRoot, recursive: true); }
    catch { }
};

Check(new AppStateV2().SchemaVersion == 5, "新状态版本不是 5。");
Check(new GlobalSettings().Language == AppLanguage.ChineseSimplified, "新配置没有默认使用中文。");
Check(new GlobalSettings().ExclusiveExpansion, "全局单窗展开没有默认开启。");
Check(!new GlobalSettings().CollapseOnOutsideClick, "窗口外点击收缩没有默认关闭。");
Check(!new GlobalSettings().ExpandOnHover, "鼠标悬浮展开没有默认关闭。");
Check(!new GlobalSettings().CollapseOnPointerLeave, "鼠标离开收缩没有默认关闭。");
Check(OrganizerInteractionMath.ShouldStartHoverExpand(
        enabled: true, station: false, expanded: false, animating: false, interactionActive: false),
    "空闲的普通收纳窗没有允许悬浮展开。");
Check(!OrganizerInteractionMath.ShouldStartHoverExpand(
        enabled: true, station: false, expanded: false, animating: false, interactionActive: true),
    "鼠标按下或长按拖动时仍然允许悬浮展开。");
Check(!OrganizerInteractionMath.ShouldStartHoverExpand(
        enabled: true, station: true, expanded: false, animating: false, interactionActive: false),
    "普通窗口悬浮状态机错误接管了中转站。");

string migrationRoot = Path.Combine(logicRoot, "Migration");
Directory.CreateDirectory(migrationRoot);
try
{
    string statePath = Path.Combine(migrationRoot, "state.json");
    await File.WriteAllTextAsync(statePath, """
        {
          "SchemaVersion": 3,
          "GlobalSettings": { "Theme": 0, "StartWithWindows": false, "Language": 2 },
          "Organizers": [
            {
              "Id": "33333333-3333-3333-3333-333333333333",
              "Name": "旧窗口",
              "PlacementMode": 1,
              "Layout": { "Mode": 0, "Rows": 3, "Columns": 3 },
              "ItemOrder": ["note:44444444444444444444444444444444"],
              "Notes": [
                {
                  "Id": "44444444-4444-4444-4444-444444444444",
                  "Name": "",
                  "Theme": 99,
                  "FontSize": 100,
                  "Placement": { "XDip": 10, "YDip": 20, "WidthDip": 10, "HeightDip": 10 }
                },
                {
                  "Id": "55555555-5555-5555-5555-555555555555",
                  "Name": "便签 1"
                }
              ]
            }
          ]
        }
        """);
    var migrationStore = new StateStore(statePath);
    AppStateV2 migrated = await migrationStore.LoadAsync();
    Check(migrated.SchemaVersion == 5 && migrated.GlobalSettings.Language == AppLanguage.Japanese &&
          !migrated.GlobalSettings.ExpandOnHover && !migrated.GlobalSettings.CollapseOnPointerLeave &&
          migrated.Organizers.Count == 1 && migrated.Organizers[0].Name == "旧窗口" &&
          migrated.Organizers[0].PlacementMode == OrganizerPlacementMode.Positioned &&
          migrated.Organizers[0].Notes.Count == 2 &&
          migrated.Organizers[0].Notes[0].Name == "便签 1" &&
          migrated.Organizers[0].Notes[1].Name == "便签 2" &&
          migrated.Organizers[0].Notes[0].Theme == NoteTheme.RainBlue &&
          migrated.Organizers[0].Notes[0].FontSize == 48 &&
          migrated.Organizers[0].Notes.All(note => !note.ShowRuledLines) &&
          migrated.Organizers[0].Notes[0].Placement is { WidthDip: 280, HeightDip: 220 },
        "版本 3 状态没有无损迁移到版本 5，或旧便签没有默认关闭横线背景。");

    migrated.GlobalSettings.Language = AppLanguage.Japanese;
    migrated.GlobalSettings.ExpandOnHover = true;
    migrated.GlobalSettings.CollapseOnPointerLeave = true;
    await migrationStore.SaveAsync(migrated);
    AppStateV2 reloaded = await migrationStore.LoadAsync();
    Check(reloaded.GlobalSettings.Language == AppLanguage.Japanese && reloaded.GlobalSettings.ExpandOnHover &&
          reloaded.GlobalSettings.CollapseOnPointerLeave,
        "版本 5 没有保留用户重新选择的语言、悬浮展开或鼠标离开收缩设置。");
}
finally
{
    Directory.Delete(migrationRoot, recursive: true);
}

var noteNames = new[] { "便签 1", "便签 3", "计划" };
Check(OrganizerNoteRules.CreateDefaultName(noteNames) == "便签 2", "便签默认名称没有复用最小空闲编号。");
Check(OrganizerNoteRules.IsNameAvailable(noteNames, " 计划 ") == false,
    "便签重命名没有阻止同一收纳窗内的重复名称。");
Check(OrganizerNoteRules.PlainTextToHtml("<计划>\r\n第二行") == "&lt;计划&gt;<br>第二行",
    "剪贴板文字没有按纯文本安全写入便签正文。");
Check(ShellDragService.RequiresNativeDrag(WidgetItemKind.Note) &&
      ShellDragService.RequiresNativeDrag(WidgetItemKind.File),
    "便签或真实文件没有进入共享外拖流程。");

string noteRoot = Path.Combine(logicRoot, "Notes");
var noteStore = new NoteStore(noteRoot);
Guid noteId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
Guid copiedNoteId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
var noteDocument = new NoteDocument { Html = "<div>正文<img src=\"data:image/png;base64,AA==\"></div>" };
await noteStore.SaveAsync(noteId, noteDocument);
Check((await noteStore.LoadAsync(noteId)).Html == noteDocument.Html, "便签正文没有从独立文件往返保存。");
await noteStore.CopyAsync(noteId, copiedNoteId);
Check((await noteStore.LoadAsync(copiedNoteId)).Html == noteDocument.Html, "复制便签没有复制正文文件。");
await noteStore.DeleteAsync(noteId);
Check(!(await noteStore.ExistsAsync(noteId)) && await noteStore.ExistsAsync(copiedNoteId),
    "删除便签正文时影响了其他便签文件。");
Guid corruptNoteId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
await File.WriteAllTextAsync(Path.Combine(noteRoot, $"{corruptNoteId:N}.json"), "not json");
bool corruptNoteRejected = false;
try { _ = await noteStore.LoadAsync(corruptNoteId); }
catch (InvalidDataException) { corruptNoteRejected = true; }
Check(corruptNoteRejected, "损坏的便签正文会被当成空白内容覆盖。");

var portableNote = new PortableNoteDocument
{
    Format = "TuckPane.Note",
    Version = 1,
    Theme = NoteTheme.WheatPaper,
    FontSize = 17,
    ShowRuledLines = true,
    Placement = new PortableNotePlacement
    {
        MonitorDevice = "DISPLAY-2",
        XDip = 120,
        YDip = 80,
        WidthDip = 420,
        HeightDip = 360
    },
    Html = "<div>第一行</div><div>第二行<img src=\"data:image/png;base64,AA==\"></div>"
};
string portablePath = await noteStore.CreatePortableStagingAsync("会议记录", portableNote);
PortableNoteDocument portableRoundTrip = await noteStore.LoadPortableAsync(portablePath);
Check(Path.GetExtension(portablePath) == ".tucknote" &&
      Path.GetFullPath(portablePath).StartsWith(
          Path.GetFullPath(AppPaths.NoteStagingRoot) + Path.DirectorySeparatorChar,
          StringComparison.OrdinalIgnoreCase) &&
      portableRoundTrip.Format == "TuckPane.Note" && portableRoundTrip.Version == 1 &&
      portableRoundTrip.Theme == NoteTheme.WheatPaper && portableRoundTrip.FontSize == 17 &&
      portableRoundTrip.ShowRuledLines &&
      portableRoundTrip.Placement is { MonitorDevice: "DISPLAY-2", XDip: 120, YDip: 80, WidthDip: 420, HeightDip: 360 } &&
      portableRoundTrip.Html == portableNote.Html,
    "便携便签没有按 UTF-8 JSON v1 在隔离暂存目录中完整往返。");
byte[] portableBytes = await File.ReadAllBytesAsync(portablePath);
using (System.Text.Json.JsonDocument portableJson = System.Text.Json.JsonDocument.Parse(portableBytes))
{
    string[] propertyNames = portableJson.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
    Check(!portableBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) &&
          propertyNames.SequenceEqual(["format", "version", "theme", "fontSize", "showRuledLines", "placement", "html"]),
        "便携便签不是无 BOM UTF-8 或没有使用固定的 JSON v1 字段。");
}

portableRoundTrip.Html = "<div>原子更新后的正文</div>";
await noteStore.SavePortableAsync(portablePath, portableRoundTrip);
Check((await noteStore.LoadPortableAsync(portablePath)).Html == portableRoundTrip.Html &&
      !Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(portablePath)!)
          .Any(path => Path.GetFileName(path).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)),
    "便携便签没有通过同目录临时文件完成原子更新。");

string movedPortablePath = Path.Combine(Path.GetDirectoryName(portablePath)!, "moved.tucknote");
File.Move(portablePath, movedPortablePath);
bool missingPortableRefused = false;
try { await noteStore.SavePortableAsync(portablePath, portableRoundTrip); }
catch (FileNotFoundException) { missingPortableRefused = true; }
catch (IOException) { missingPortableRefused = true; }
Check(missingPortableRefused && !File.Exists(portablePath) && File.Exists(movedPortablePath),
    "外部移动便携便签后，保存操作在旧路径重建了文件。");

string invalidPortablePath = Path.Combine(logicRoot, "invalid.tucknote");
string[] invalidPortableDocuments =
[
    "not json",
    """{"format":"TuckPane.Note","version":2,"theme":0,"fontSize":14,"showRuledLines":false,"placement":null,"html":""}""",
    """{"format":"Other.Note","version":1,"theme":0,"fontSize":14,"showRuledLines":false,"placement":null,"html":""}""",
    """{"format":"TuckPane.Note","version":1,"theme":99,"fontSize":14,"showRuledLines":false,"placement":null,"html":""}""",
    """{"format":"TuckPane.Note","version":1,"theme":0,"fontSize":7,"showRuledLines":false,"placement":null,"html":""}""",
    """{"format":"TuckPane.Note","version":1,"theme":0,"fontSize":14,"showRuledLines":false,"placement":{"monitorDevice":"","xDip":0,"yDip":0,"widthDip":279,"heightDip":300},"html":""}""",
    """{"format":"TuckPane.Note","version":1,"theme":0,"fontSize":14,"placement":null,"html":""}""",
    """{"format":"TuckPane.Note","version":1,"theme":0,"fontSize":14,"showRuledLines":false,"placement":null,"html":"","extra":true}"""
];
foreach (string invalidPortableDocument in invalidPortableDocuments)
{
    await File.WriteAllTextAsync(invalidPortablePath, invalidPortableDocument, new System.Text.UTF8Encoding(false));
    bool rejected = false;
    try { _ = await noteStore.LoadPortableAsync(invalidPortablePath); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, $"损坏或不兼容的便携便签未被严格拒绝：{invalidPortableDocument}");
}

await using (FileStream oversizedPortable = new(invalidPortablePath, FileMode.Create, FileAccess.Write, FileShare.None))
    oversizedPortable.SetLength(64L * 1024 * 1024 + 1);
bool oversizedPortableRejected = false;
try { _ = await noteStore.LoadPortableAsync(invalidPortablePath); }
catch (InvalidDataException) { oversizedPortableRejected = true; }
Check(oversizedPortableRejected, "超过 64 MiB 的便携便签未被读取边界拒绝。");
Check(!new PortableNoteDocument().ShowRuledLines,
    "旧便签进入便携格式时没有默认关闭横线背景。");
foreach ((string sourceName, string expectedName) in new[]
{
    ("", "便签.tucknote"),
    ("bad:name. ", "bad_name.tucknote"),
    ("CON", "_CON.tucknote"),
    ("Lpt1.txt", "_Lpt1.txt.tucknote")
})
{
    Check(NoteStore.CreatePortableFileName(sourceName) == expectedName,
        $"便携便签文件名净化错误：{sourceName} -> {NoteStore.CreatePortableFileName(sourceName)}");
}

string staleStaging = Path.Combine(AppPaths.NoteStagingRoot, "11111111111111111111111111111111");
string unrelatedStaging = Path.Combine(AppPaths.NoteStagingRoot, "keep-me");
string stagingSentinel = Path.Combine(AppPaths.NoteStagingRoot, "keep-me.txt");
Directory.CreateDirectory(staleStaging);
Directory.CreateDirectory(unrelatedStaging);
await File.WriteAllTextAsync(Path.Combine(staleStaging, "old.tucknote"), "old");
await File.WriteAllTextAsync(Path.Combine(unrelatedStaging, "sentinel.txt"), "keep");
await File.WriteAllTextAsync(stagingSentinel, "keep");
AppPaths.CleanupNoteStaging();
Check(!Directory.Exists(staleStaging) &&
      Directory.Exists(unrelatedStaging) && File.Exists(Path.Combine(unrelatedStaging, "sentinel.txt")) &&
      File.Exists(stagingSentinel),
    "启动暂存清理越过了 NoteStagingRoot 的直接 GUID 子目录边界。");
AppPaths.EnsureCreated();
string activeStaging = Path.Combine(AppPaths.NoteStagingRoot, "22222222222222222222222222222222");
Directory.CreateDirectory(activeStaging);
await File.WriteAllTextAsync(Path.Combine(activeStaging, "active.tucknote"), "active");
AppPaths.EnsureCreated();
Check(File.Exists(Path.Combine(activeStaging, "active.tucknote")),
    "重复 EnsureCreated 清理了当前进程正在使用的便签暂存文件。");

string newStoragePath = AppPaths.CreateStorageRelativePath(
    "Storage",
    Guid.Parse("22222222-2222-2222-2222-222222222222"));
Check(!Path.GetFileName(newStoragePath).Equals("Items", StringComparison.OrdinalIgnoreCase),
    "新建默认目录仍包含末尾 Items 层。");

string customStorage = Path.Combine(logicRoot, "SelectedStorage");
Directory.CreateDirectory(customStorage);
await File.WriteAllTextAsync(Path.Combine(customStorage, "existing.txt"), "existing");
Check(AppPaths.ValidateCustomStoragePath(customStorage) == Path.GetFullPath(customStorage),
    "手选目录没有被直接作为最终存储目录。");
Check(new StorageService(customStorage, createIfMissing: false).ReadItems().Count == 1,
    "手选目录的已有顶层内容没有直接显示。");
Check(AppPaths.PathsOverlap(customStorage, Path.Combine(customStorage, "Child")),
    "父子收纳目录重叠没有被识别。");
Check(!AppPaths.PathsOverlap(customStorage, Path.Combine(logicRoot, "Sibling")),
    "无关目录被错误判定为重叠。");
bool rejectedProtectedPath = false;
try { _ = AppPaths.ValidateCustomStoragePath(logicRoot); }
catch (InvalidOperationException) { rejectedProtectedPath = true; }
Check(rejectedProtectedPath, "危险上级目录没有被拒绝。");

var oldStorageState = new AppStateV2
{
    Organizers = [new OrganizerDefinition { StorageRelativePath = Path.Combine("Windows", "Legacy-11111111", "Items") }]
};
StateStore.Normalize(oldStorageState);
Check(Path.GetFileName(oldStorageState.Organizers[0].StorageRelativePath).Equals("Items", StringComparison.OrdinalIgnoreCase),
    "旧版 Items 存储路径被意外迁移。");

TransferOutcome exportOutcome = await new StorageService(customStorage, createIfMissing: false)
    .ExportToDesktopAsync("Direct storage", null, CancellationToken.None);
Check(exportOutcome.Status == TransferStatus.Moved && !Directory.Exists(customStorage) &&
      exportOutcome.DestinationPath is not null && File.Exists(Path.Combine(exportOutcome.DestinationPath, "existing.txt")),
    "手选目录没有作为整个目录导出并删除原目录。");

string emptyCustomStorage = Path.Combine(logicRoot, "EmptySelectedStorage");
Directory.CreateDirectory(emptyCustomStorage);
TransferOutcome emptyExportOutcome = await new StorageService(
        emptyCustomStorage,
        createIfMissing: false,
        exportEmptyDirectory: true)
    .ExportToDesktopAsync("Empty direct storage", null, CancellationToken.None);
Check(emptyExportOutcome.Status == TransferStatus.Moved && !Directory.Exists(emptyCustomStorage) &&
      emptyExportOutcome.DestinationPath is not null && Directory.Exists(emptyExportOutcome.DestinationPath),
    "空的手选目录没有整体导出到桌面。");

string pasteSourceRoot = Path.Combine(logicRoot, "PasteSources");
string pasteStorageRoot = Path.Combine(logicRoot, "PasteStorage");
Directory.CreateDirectory(pasteSourceRoot);
string copiedFileSource = Path.Combine(pasteSourceRoot, "note.txt");
await File.WriteAllTextAsync(copiedFileSource, "copy-source");
string copiedFolderSource = Path.Combine(pasteSourceRoot, "Folder");
Directory.CreateDirectory(Path.Combine(copiedFolderSource, "Nested"));
await File.WriteAllTextAsync(Path.Combine(copiedFolderSource, "Nested", "inside.txt"), "inside");
var pasteStorage = new StorageService(pasteStorageRoot);
IReadOnlyList<TransferOutcome> copiedOutcomes = await pasteStorage.CopyBatchAsync(
    [copiedFileSource, copiedFolderSource],
    null,
    CancellationToken.None);
Check(copiedOutcomes.All(outcome => outcome.Status == TransferStatus.Copied) &&
      File.Exists(copiedFileSource) && Directory.Exists(copiedFolderSource) &&
      File.Exists(Path.Combine(pasteStorageRoot, "note.txt")) &&
      File.Exists(Path.Combine(pasteStorageRoot, "Folder", "Nested", "inside.txt")),
    "剪贴板复制导入没有保留源项目或递归复制文件夹。");
IReadOnlyList<TransferOutcome> duplicateCopy = await pasteStorage.CopyBatchAsync(
    [copiedFileSource],
    null,
    CancellationToken.None);
Check(duplicateCopy.Single().Status == TransferStatus.Copied && File.Exists(Path.Combine(pasteStorageRoot, "note 2.txt")),
    "复制导入重名时没有自动编号。");

string movedFileSource = Path.Combine(pasteSourceRoot, "cut.txt");
await File.WriteAllTextAsync(movedFileSource, "cut-source");
IReadOnlyList<TransferOutcome> movedOutcomes = await pasteStorage.ImportBatchAsync(
    [movedFileSource],
    null,
    CancellationToken.None);
Check(movedOutcomes.Single().Status == TransferStatus.Moved && !File.Exists(movedFileSource) &&
      File.Exists(Path.Combine(pasteStorageRoot, "cut.txt")),
    "剪切粘贴没有复用移动导入路径。");

string executableSource = Environment.ProcessPath ?? throw new InvalidOperationException("无法取得测试程序路径。");
IReadOnlyList<TransferOutcome> executablePaste = await pasteStorage.CopyBatchAsync(
    [executableSource],
    null,
    CancellationToken.None);
Check(executablePaste.Single().Status == TransferStatus.ShortcutCreated && File.Exists(executableSource) &&
      executablePaste.Single().DestinationPath is string shortcut && File.Exists(shortcut),
    "粘贴程序时没有保留程序本体并创建快捷方式。");

string createdFolder = pasteStorage.CreateUniqueFolder("New Folder");
string numberedFolder = pasteStorage.CreateUniqueFolder("New Folder");
Check(Path.GetFileName(createdFolder) == "New Folder" && Path.GetFileName(numberedFolder) == "New Folder 2",
    "新建文件夹重名时没有自动编号。");
foreach (string invalidName in new[] { "", "bad:name", "trailing.", "CON", "LPT1.txt" })
{
    bool rejected = false;
    try { _ = StorageService.ValidateNewFolderName(invalidName); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, $"非法文件夹名称未被拒绝：{invalidName}");
}

using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    bool cancelledCopy = false;
    try { _ = await pasteStorage.CopyBatchAsync([copiedFileSource], null, cancelled.Token); }
    catch (OperationCanceledException) { cancelledCopy = true; }
    Check(cancelledCopy && !Directory.EnumerateFileSystemEntries(pasteStorageRoot)
            .Any(path => Path.GetFileName(path).StartsWith(".glassfolder-staging-", StringComparison.OrdinalIgnoreCase)),
        "复制取消后留下了临时文件。");
}

string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "TuckPane.ico");
string shortcutRoot = Path.Combine(logicRoot, "Shortcut");
Directory.CreateDirectory(shortcutRoot);
string shortcutIconPath = Path.Combine(shortcutRoot, "TuckPane.ico");
File.Copy(iconPath, shortcutIconPath);
string internetShortcutPath = Path.Combine(shortcutRoot, "Steam.url");
await File.WriteAllTextAsync(internetShortcutPath, """
    [InternetShortcut]
    URL=steam://rungameid/431960
    IconFile=TuckPane.ico
    IconIndex=0
    """);
IconCacheService.IconSnapshot expectedIcon = IconCacheService.ExtractShellIconPixels(shortcutIconPath);
IconCacheService.IconSnapshot shortcutIcon = IconCacheService.ExtractShellIconPixels(internetShortcutPath);
long iconDifference = 0;
long iconRange = 0;
for (int index = 0; index < expectedIcon.Pixels.Length; index += 4)
{
    if (expectedIcon.Pixels[index + 3] == 0 && shortcutIcon.Pixels[index + 3] == 0) continue;
    for (int channel = 0; channel < 4; channel++)
    {
        iconDifference += Math.Abs(expectedIcon.Pixels[index + channel] - shortcutIcon.Pixels[index + channel]);
        iconRange += byte.MaxValue;
    }
}
double iconSimilarity = iconRange == 0 ? 0 : 1d - (double)iconDifference / iconRange;
Check(expectedIcon.Size == shortcutIcon.Size && iconSimilarity >= .95,
    $"Steam .url 没有使用声明图标，相似度仅 {iconSimilarity:F4}。");

string copyName = OrganizerInteractionMath.CreateCopyName(
    "学习",
    ["学习", "学习 - 副本", "学习 - 副本 (2)"],
    " - 副本");
Check(copyName == "学习 - 副本 (3)", "副本名称编号错误。");

var source = new OrganizerDefinition
{
    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
    Name = "学习",
    ThemeOverride = GlassTheme.SolidDark,
    PlacementMode = OrganizerPlacementMode.Positioned,
    DockEdge = OrganizerDockEdge.Bottom,
    Layout = new OrganizerLayout { Rows = 4, Columns = 5 },
    CompactScale = 1.8,
    CanvasScale = .72,
    ItemScale = 1.15,
    NameScale = .9,
    ManualCanvasBaseWidthDip = 800,
    ManualCanvasBaseHeightDip = 600,
    Position = new WidgetPosition { MonitorDevice = "test" },
    StorageAbsolutePath = @"D:\source\Items",
    ItemOrder = ["one.txt"]
};
OrganizerDefinition copy = OrganizerInteractionMath.CopySettings(source, copyName);
Check(copy.Id != source.Id && copy.Name == copyName, "副本身份未重建。");
Check(copy.Layout.Rows == 4 && copy.Layout.Columns == 5 && copy.ThemeOverride == source.ThemeOverride,
    "外观设置未完整复制。");
Check(copy.DockEdge == OrganizerDockEdge.Bottom, "贴靠边设置未复制。");
Check(copy.ManualCanvasBaseWidthDip == 800 && copy.ManualCanvasBaseHeightDip == 600,
    "手动画布形状未复制。");
Check(copy.Position is null && copy.StorageAbsolutePath is null && copy.StorageRelativePath.Length == 0 && copy.ItemOrder.Count == 0,
    "副本错误复制了位置、目录或文件顺序。");

CanvasResizeEdge[] edges =
[
    CanvasResizeEdge.Left,
    CanvasResizeEdge.Top,
    CanvasResizeEdge.Right,
    CanvasResizeEdge.Bottom,
    CanvasResizeEdge.Left | CanvasResizeEdge.Top,
    CanvasResizeEdge.Right | CanvasResizeEdge.Top,
    CanvasResizeEdge.Left | CanvasResizeEdge.Bottom,
    CanvasResizeEdge.Right | CanvasResizeEdge.Bottom
];
foreach (CanvasResizeEdge edge in edges)
{
    double deltaX = edge.HasFlag(CanvasResizeEdge.Left) ? -40 : edge.HasFlag(CanvasResizeEdge.Right) ? 40 : 0;
    double deltaY = edge.HasFlag(CanvasResizeEdge.Top) ? -30 : edge.HasFlag(CanvasResizeEdge.Bottom) ? 30 : 0;
    double factor = OrganizerInteractionMath.CalculateResizeFactor(edge, deltaX, deltaY, 400, 300);
    Check(Math.Abs(factor - 1.2) < .0001, $"{edge} 缩放倍率错误：{factor}");
    double width = 400 * factor;
    double height = 300 * factor;
    Check(Math.Abs(width / height - 4d / 3d) < .0001, $"{edge} 未保持宽高比。");
}

(int left, int top, int roundedWidth, int roundedHeight) =
    OrganizerInteractionMath.CreateCenteredBounds(1000, 600, 487.3, 365.475);
Check(Math.Abs((left + roundedWidth / 2d) - 1000) <= .5 &&
      Math.Abs((top + roundedHeight / 2d) - 600) <= .5,
    "整数像素取整后的缩放中心误差超过 1 像素。");
Check(Math.Abs(roundedWidth - roundedHeight * 4d / 3d) <= 1,
    "整数像素取整后的宽高比误差超过 1 像素。");

Check(OrganizerInteractionMath.ApplyWheelSteps(1, 1, .5, 1.65) == 1.05, "滚轮放大步长错误。");
Check(OrganizerInteractionMath.ApplyWheelSteps(1, -1, .5, 1.65) == .95, "滚轮缩小步长错误。");
Check(OrganizerInteractionMath.ApplyWheelSteps(1.64, 1, .5, 1.65) == 1.65, "滚轮上限错误。");
Check(OrganizerInteractionMath.ApplyWheelSteps(.51, -1, .5, 1.65) == .5, "滚轮下限错误。");

var layout = new OrganizerLayout { Rows = 3, Columns = 3 };
(double minimumWidth, double minimumHeight) = DisplayPlacementService.CalculateMinimumExpandedSizeDip(layout, .5);
Check(DisplayPlacementService.CalculateMaximumItemScaleForExpandedSize(layout, minimumWidth, minimumHeight) == .5,
    "最小画布没有把内容比例限制为 50%。");
Check(DisplayPlacementService.CalculateMaximumItemScaleForExpandedSize(layout, minimumWidth * 2, minimumHeight * 2) > .5,
    "放大画布后内容比例上限没有提高。");

var layoutLimits = new AppStateV2
{
    Organizers =
    [
        new OrganizerDefinition
        {
            PlacementMode = OrganizerPlacementMode.Floating,
            Layout = new OrganizerLayout { Rows = 99, Columns = 1 }
        },
        new OrganizerDefinition
        {
            PlacementMode = OrganizerPlacementMode.Station,
            Layout = new OrganizerLayout { Rows = 99, Columns = 0 }
        },
        new OrganizerDefinition
        {
            PlacementMode = OrganizerPlacementMode.Station,
            DockEdge = OrganizerDockEdge.Left,
            Layout = new OrganizerLayout { Rows = 1, Columns = 99 }
        }
    ]
};
StateStore.Normalize(layoutLimits);
Check(layoutLimits.Organizers[0].Layout.Rows == 6 && layoutLimits.Organizers[0].Layout.Columns == 2,
    "普通窗口没有保持 2–6 行列限制。");
Check(layoutLimits.Organizers[1].Layout.Rows == 9 && layoutLimits.Organizers[1].Layout.Columns == 1 &&
      layoutLimits.Organizers[2].Layout.Rows == 1 && layoutLimits.Organizers[2].Layout.Columns == 9,
    "中转站没有使用 1–9 行列限制。");

var invalidPair = new AppStateV2
{
    Organizers = [new OrganizerDefinition { ManualCanvasBaseWidthDip = 800 }]
};
StateStore.Normalize(invalidPair);
Check(invalidPair.Organizers[0].ManualCanvasBaseWidthDip is null &&
      invalidPair.Organizers[0].ManualCanvasBaseHeightDip is null,
    "不完整的手动画布尺寸未被清理。");

var stationManualCanvas = new AppStateV2
{
    Organizers =
    [
        new OrganizerDefinition
        {
            PlacementMode = OrganizerPlacementMode.Station,
            ManualCanvasBaseWidthDip = 867.5,
            ManualCanvasBaseHeightDip = 2564.6
        }
    ]
};
StateStore.Normalize(stationManualCanvas);
Check(stationManualCanvas.Organizers[0].ManualCanvasBaseWidthDip is null &&
      stationManualCanvas.Organizers[0].ManualCanvasBaseHeightDip is null,
    "中转站仍然保留会破坏内容自适应的自由长宽比。");

var compactScaleLimits = new AppStateV2
{
    Organizers =
    [
        new OrganizerDefinition { Name = "悬浮下限", PlacementMode = OrganizerPlacementMode.Floating, CompactScale = .5 },
        new OrganizerDefinition { Name = "悬浮上限", PlacementMode = OrganizerPlacementMode.Floating, CompactScale = 4 },
        new OrganizerDefinition { Name = "定位下限", PlacementMode = OrganizerPlacementMode.Positioned, CompactScale = .5 },
        new OrganizerDefinition { Name = "定位上限", PlacementMode = OrganizerPlacementMode.Positioned, CompactScale = 4 },
        new OrganizerDefinition { Name = "定位旧值", PlacementMode = OrganizerPlacementMode.Positioned, CompactScale = 1.8 }
    ]
};
StateStore.Normalize(compactScaleLimits);
Check(compactScaleLimits.Organizers[0].CompactScale == 1.2, "悬浮入口下限不是 120%。");
Check(compactScaleLimits.Organizers[1].CompactScale == 3, "悬浮入口上限不是 300%。");
Check(compactScaleLimits.Organizers[2].CompactScale == 1.2, "定位入口下限不是 120%。");
Check(compactScaleLimits.Organizers[3].CompactScale == 1.8, "定位入口上限不是 180%。");
Check(compactScaleLimits.Organizers[4].CompactScale == 1.8, "旧定位入口 180% 没有保持不变。");

var organizerLimits = new AppStateV2
{
    Organizers = Enumerable.Range(0, 13)
        .Select(index => new OrganizerDefinition { Name = $"普通 {index}" })
        .Concat(Enum.GetValues<OrganizerDockEdge>().Select(edge => new OrganizerDefinition
        {
            Name = $"{edge} 中转站",
            PlacementMode = OrganizerPlacementMode.Station,
            DockEdge = edge
        }))
        .ToList()
};
StateStore.Normalize(organizerLimits);
Check(organizerLimits.Organizers.Count(item => item.PlacementMode != OrganizerPlacementMode.Station) == 12 &&
      organizerLimits.Organizers.Count(item => item.PlacementMode == OrganizerPlacementMode.Station) == 4,
    "12 个普通窗口和 4 个中转站没有使用独立上限。");

var duplicateStationEdge = new AppStateV2
{
    Organizers =
    [
        new OrganizerDefinition { PlacementMode = OrganizerPlacementMode.Station, DockEdge = OrganizerDockEdge.Left },
        new OrganizerDefinition { PlacementMode = OrganizerPlacementMode.Station, DockEdge = OrganizerDockEdge.Left }
    ]
};
StateStore.Normalize(duplicateStationEdge);
Check(duplicateStationEdge.Organizers.Count(item => item.PlacementMode == OrganizerPlacementMode.Station) == 1,
    "同一边保留了多个中转站。");

var stationDisplay = new DisplayInfo(
    "station-test",
    new NativeMethods.RECT { Left = 100, Top = 20, Right = 2020, Bottom = 1100 },
    new NativeMethods.RECT { Left = 100, Top = 60, Right = 2020, Bottom = 1100 },
    1);

var scaledStationDisplay = new DisplayInfo(
    "station-scaled-test",
    new NativeMethods.RECT { Left = 0, Top = 0, Right = 2400, Bottom = 1350 },
    new NativeMethods.RECT { Left = 0, Top = 0, Right = 2400, Bottom = 1300 },
    1.25);
NativeMethods.RECT centeredDialog = DisplayPlacementService.CalculateCenteredDialogBounds(scaledStationDisplay);
Check(centeredDialog.Width == 550 && centeredDialog.Height == 350 &&
      Math.Abs(centeredDialog.Left + centeredDialog.Width / 2d - 1200) <= .5 &&
      Math.Abs(centeredDialog.Top + centeredDialog.Height / 2d - 650) <= .5,
    "独立对话框没有按目标显示器 DPI 居中。");
var smallDialogDisplay = new DisplayInfo(
    "dialog-small-test",
    new NativeMethods.RECT { Left = 100, Top = 200, Right = 600, Bottom = 500 },
    new NativeMethods.RECT { Left = 100, Top = 200, Right = 600, Bottom = 500 },
    1);
NativeMethods.RECT clampedDialog = DisplayPlacementService.CalculateCenteredDialogBounds(smallDialogDisplay);
Check(clampedDialog.Left == 130 && clampedDialog.Top == 224 &&
      clampedDialog.Right == 570 && clampedDialog.Bottom == 476,
    "独立对话框没有保留 24 DIP 工作区边距。 ");

foreach (OrganizerDockEdge edge in Enum.GetValues<OrganizerDockEdge>())
{
    StationTransitionFrame start = StationTransitionMath.GetFrame(edge, 300, 500, 0, .8, reducedMotion: false);
    StationTransitionFrame middle = StationTransitionMath.GetFrame(edge, 300, 500, .5, .8, reducedMotion: false);
    StationTransitionFrame end = StationTransitionMath.GetFrame(edge, 300, 500, 1, .8, reducedMotion: false);
    Check(end.ClipLeft == 0 && end.ClipTop == 0 && end.ClipRight == 300 && end.ClipBottom == 500 &&
          end.TranslationX == 0 && end.TranslationY == 0 && end.Opacity == 1,
        $"{edge} 中转站展开终点不是完整画布。 ");
    Check(start.TranslationX == 0 || start.TranslationY == 0,
        $"{edge} 中转站仍包含斜向位移。 ");
    Check(edge switch
    {
        OrganizerDockEdge.Left => start.ClipLeft == 0 && start.ClipRight == .8 && middle.ClipRight > start.ClipRight && start.TranslationX < 0,
        OrganizerDockEdge.Top => start.ClipTop == 0 && start.ClipBottom == .8 && middle.ClipBottom > start.ClipBottom && start.TranslationY < 0,
        OrganizerDockEdge.Right => start.ClipRight == 300 && Math.Abs(start.ClipLeft - 299.2) < .0001 && middle.ClipLeft < start.ClipLeft && start.TranslationX > 0,
        _ => start.ClipBottom == 500 && Math.Abs(start.ClipTop - 499.2) < .0001 && middle.ClipTop < start.ClipTop && start.TranslationY > 0
    }, $"{edge} 中转站没有从所属边缘单轴揭开。 ");
    StationTransitionFrame reduced = StationTransitionMath.GetFrame(edge, 300, 500, .4, .8, reducedMotion: true);
    Check(reduced.ClipLeft == 0 && reduced.ClipTop == 0 && reduced.ClipRight == 300 && reduced.ClipBottom == 500 &&
          reduced.TranslationX == 0 && reduced.TranslationY == 0 && reduced.Opacity == .4,
        $"{edge} 减少动态效果仍包含位移或裁剪。 ");
}

var oneColumnStationLayout = new OrganizerLayout { Rows = 6, Columns = 1 };
NativeMethods.RECT oneColumnStation = DisplayPlacementService.CalculateStationBounds(
    scaledStationDisplay,
    OrganizerDockEdge.Right,
    oneColumnStationLayout,
    .1,
    .5);
NativeMethods.RECT twoColumnStation = DisplayPlacementService.CalculateStationBounds(
    scaledStationDisplay,
    OrganizerDockEdge.Right,
    new OrganizerLayout { Rows = 6, Columns = 2 },
    .1,
    .5);
Check(oneColumnStation.Width == 97 && twoColumnStation.Width == 178,
    $"侧边中转站没有按内容贴合：一列={oneColumnStation.Width}px，两列={twoColumnStation.Width}px。");
Check(oneColumnStation.Right == scaledStationDisplay.Work.Right,
    "一列中转站没有保持右侧贴边。");

NativeMethods.RECT enlargedStation = DisplayPlacementService.CalculateStationBounds(
    scaledStationDisplay,
    OrganizerDockEdge.Right,
    oneColumnStationLayout,
    .1,
    DisplayPlacementService.MaximumItemScale);
Check(enlargedStation.Width > oneColumnStation.Width && enlargedStation.Height > oneColumnStation.Height &&
      enlargedStation.Right == scaledStationDisplay.Work.Right && enlargedStation.Height <= scaledStationDisplay.Work.Height,
    "中转站内容放大后没有同步贴合外框或超出了工作区。");
NativeMethods.RECT restoredStation = DisplayPlacementService.CalculateStationBounds(
    scaledStationDisplay,
    OrganizerDockEdge.Right,
    oneColumnStationLayout,
    .1,
    .5);
Check(restoredStation.Width == oneColumnStation.Width && restoredStation.Height == oneColumnStation.Height,
    "中转站内容缩小后没有恢复内容贴合尺寸。");
NativeMethods.RECT legacyManualStation = DisplayPlacementService.CalculateStationBounds(
    scaledStationDisplay,
    OrganizerDockEdge.Right,
    oneColumnStationLayout,
    .35,
    .5,
    manualCanvasBaseWidthDip: 867.5,
    manualCanvasBaseHeightDip: 2564.6);
Check(legacyManualStation.Width == oneColumnStation.Width && legacyManualStation.Height == oneColumnStation.Height,
    "旧的中转站自由长宽比仍然覆盖内容自适应尺寸。");

var topNineColumnLayout = new OrganizerLayout { Rows = 1, Columns = 9 };
NativeMethods.RECT topNineColumnStation = DisplayPlacementService.CalculateStationBounds(
    scaledStationDisplay,
    OrganizerDockEdge.Top,
    topNineColumnLayout,
    .1,
    .5);
NativeMethods.RECT topNineColumnStationLargeItems = DisplayPlacementService.CalculateStationBounds(
    scaledStationDisplay,
    OrganizerDockEdge.Top,
    topNineColumnLayout,
    .1,
    DisplayPlacementService.MaximumItemScale);
Check(topNineColumnStation.Width == 750 && topNineColumnStation.Height == 98,
    $"顶部 1×9 中转站没有按单行内容贴合：{topNineColumnStation.Width}×{topNineColumnStation.Height}px。");
Check(topNineColumnStationLargeItems.Width > topNineColumnStation.Width &&
      topNineColumnStationLargeItems.Height > topNineColumnStation.Height,
    "顶部 1×9 中转站内容放大后外框没有同步贴合。");
(double stationCellWidth, double stationCellHeight) = DisplayPlacementService.CalculateItemCellSizeDip(
    582,
    54,
    topNineColumnLayout);
int fixedColumns = (int)Math.Floor((582 + DisplayPlacementService.ItemGapDip) /
    (stationCellWidth + DisplayPlacementService.ItemGapDip));
Check(stationCellWidth == stationCellHeight && fixedColumns == 9,
    "放大内容后顶部 1×9 中转站没有保持 9 个固定列。");
(double normalCellWidth, double normalCellHeight) = DisplayPlacementService.CalculateItemCellSizeDip(
    582,
    54,
    topNineColumnLayout);
Check(normalCellWidth == stationCellWidth && normalCellHeight == stationCellHeight,
    "相同网格的普通窗口与中转站使用了不同的固定单元格尺寸。");

Check(ShellDragService.ClassifyOutcome(false, 1) == ShellDragOutcome.ExternalCopied &&
      ShellDragService.ClassifyOutcome(false, 2) == ShellDragOutcome.ExternalMoved &&
      ShellDragService.ClassifyOutcome(false, 4) == ShellDragOutcome.ExternalLinked &&
      ShellDragService.ClassifyOutcome(true, 1) == ShellDragOutcome.DesktopRequested &&
      ShellDragService.ClassifyOutcome(false, 0) == ShellDragOutcome.Cancelled,
    "Shell 拖放没有按目标返回的复制、移动或链接效果分类。");
IntPtr packedRelayPoint = DragMessageRelay.PackClientPosition(new NativeMethods.POINT { X = -25, Y = 320 });
Check(unchecked((short)(packedRelayPoint.ToInt64() & 0xFFFF)) == -25 &&
      unchecked((short)((packedRelayPoint.ToInt64() >> 16) & 0xFFFF)) == 320,
    "Shell 拖放转发消息没有保留窗口外的真实客户区坐标。");

foreach (OrganizerDockEdge edge in Enum.GetValues<OrganizerDockEdge>())
{
    NativeMethods.RECT stationBounds = DisplayPlacementService.CalculateStationBounds(
        stationDisplay,
        edge,
        new OrganizerLayout { Rows = 3, Columns = 4 },
        1,
        1,
        position: null,
        manualCanvasBaseWidthDip: null,
        manualCanvasBaseHeightDip: null);
    Check(stationBounds.Left >= stationDisplay.Work.Left && stationBounds.Top >= stationDisplay.Work.Top &&
          stationBounds.Right <= stationDisplay.Work.Right && stationBounds.Bottom <= stationDisplay.Work.Bottom,
        $"{edge} 中转站超出工作区。");
    Check(edge switch
    {
        OrganizerDockEdge.Left => stationBounds.Left == stationDisplay.Work.Left &&
            Math.Abs(stationBounds.Top + stationBounds.Height / 2d - (stationDisplay.Work.Top + stationDisplay.Work.Height / 2d)) <= .5,
        OrganizerDockEdge.Top => stationBounds.Top == stationDisplay.Work.Top &&
            Math.Abs(stationBounds.Left + stationBounds.Width / 2d - (stationDisplay.Work.Left + stationDisplay.Work.Width / 2d)) <= .5,
        OrganizerDockEdge.Right => stationBounds.Right == stationDisplay.Work.Right &&
            Math.Abs(stationBounds.Top + stationBounds.Height / 2d - (stationDisplay.Work.Top + stationDisplay.Work.Height / 2d)) <= .5,
        _ => stationBounds.Bottom == stationDisplay.Work.Bottom &&
            Math.Abs(stationBounds.Left + stationBounds.Width / 2d - (stationDisplay.Work.Left + stationDisplay.Work.Width / 2d)) <= .5
    }, $"{edge} 中转站没有贴边居中。");
    int segmentX = stationBounds.Left + stationBounds.Width / 2;
    int segmentY = stationBounds.Top + stationBounds.Height / 2;
    NativeMethods.POINT hotPoint = edge switch
    {
        OrganizerDockEdge.Left => new() { X = stationDisplay.Monitor.Left + 3, Y = segmentY },
        OrganizerDockEdge.Top => new() { X = segmentX, Y = stationDisplay.Monitor.Top + 3 },
        OrganizerDockEdge.Right => new() { X = stationDisplay.Monitor.Right - 4, Y = segmentY },
        _ => new() { X = segmentX, Y = stationDisplay.Monitor.Bottom - 4 }
    };
    NativeMethods.POINT coldPoint = edge switch
    {
        OrganizerDockEdge.Left => hotPoint with { X = stationDisplay.Monitor.Left + 4 },
        OrganizerDockEdge.Top => hotPoint with { Y = stationDisplay.Monitor.Top + 4 },
        OrganizerDockEdge.Right => hotPoint with { X = stationDisplay.Monitor.Right - 5 },
        _ => hotPoint with { Y = stationDisplay.Monitor.Bottom - 5 }
    };
    NativeMethods.POINT formerOutsideSegment = edge is OrganizerDockEdge.Left or OrganizerDockEdge.Right
        ? hotPoint with { Y = stationBounds.Top > stationDisplay.Monitor.Top ? stationDisplay.Monitor.Top : stationDisplay.Monitor.Bottom - 1 }
        : hotPoint with { X = stationBounds.Left > stationDisplay.Monitor.Left ? stationDisplay.Monitor.Left : stationDisplay.Monitor.Right - 1 };
    NativeMethods.POINT outsideDisplay = edge is OrganizerDockEdge.Left or OrganizerDockEdge.Right
        ? hotPoint with { Y = stationDisplay.Monitor.Bottom }
        : hotPoint with { X = stationDisplay.Monitor.Right };
    Check(DisplayPlacementService.IsStationHotZone(hotPoint, stationDisplay, edge) &&
          DisplayPlacementService.IsStationHotZone(formerOutsideSegment, stationDisplay, edge) &&
          !DisplayPlacementService.IsStationHotZone(coldPoint, stationDisplay, edge) &&
          !DisplayPlacementService.IsStationHotZone(outsideDisplay, stationDisplay, edge),
        $"{edge} 没有使用对应显示器的整条 4px 物理屏幕边缘热区。");
}

var quarterAnchor = new WidgetPosition
{
    MonitorDevice = "old-display",
    XDip = 250,
    YDip = 250,
    SavedWorkAreaWidthDip = 1000,
    SavedWorkAreaHeightDip = 1000
};
NativeMethods.RECT proportionalAnchor = DisplayPlacementService.CalculateStationAnchor(
    stationDisplay,
    OrganizerDockEdge.Right,
    quarterAnchor);
Check(proportionalAnchor.Left == stationDisplay.Work.Right - 1 &&
      proportionalAnchor.Top == stationDisplay.Work.Top + stationDisplay.Work.Height / 4,
    "中转站没有按原工作区比例恢复沿边位置。");
NativeMethods.RECT anchoredBounds = DisplayPlacementService.CalculateStationBounds(
    stationDisplay,
    OrganizerDockEdge.Right,
    new OrganizerLayout { Rows = 1, Columns = 1 },
    .1,
    .5,
    quarterAnchor);
Check(anchoredBounds.Right == stationDisplay.Work.Right &&
      Math.Abs(anchoredBounds.Top + anchoredBounds.Height / 2d - proportionalAnchor.Top) <= .5,
    "中转站画布没有围绕保存锚点贴边。");
NativeMethods.RECT verticalDrag = DisplayPlacementService.CalculateStationDraggedBounds(
    anchoredBounds,
    new NativeMethods.POINT { X = 1900, Y = 300 },
    new NativeMethods.POINT { X = 1200, Y = 5000 },
    stationDisplay,
    OrganizerDockEdge.Right);
Check(verticalDrag.Right == stationDisplay.Work.Right && verticalDrag.Bottom == stationDisplay.Work.Bottom &&
      verticalDrag.Width == anchoredBounds.Width,
    "右侧中转站拖动没有固定贴边、仅纵向移动并限制在工作区。");
NativeMethods.RECT horizontalDrag = DisplayPlacementService.CalculateStationDraggedBounds(
    anchoredBounds,
    new NativeMethods.POINT { X = 1900, Y = 300 },
    new NativeMethods.POINT { X = -5000, Y = 900 },
    stationDisplay,
    OrganizerDockEdge.Top);
Check(horizontalDrag.Left == stationDisplay.Work.Left && horizontalDrag.Top == stationDisplay.Work.Top &&
      horizontalDrag.Height == anchoredBounds.Height,
    "顶部中转站拖动没有固定贴边、仅横向移动并限制在工作区。");
WidgetPosition capturedAnchor = DisplayPlacementService.CaptureStationPosition(
    stationDisplay,
    OrganizerDockEdge.Right,
    anchoredBounds);
Check(capturedAnchor.MonitorDevice == stationDisplay.Device && capturedAnchor.SavedWorkAreaHeightDip == stationDisplay.Work.Height &&
      Math.Abs(capturedAnchor.YDip - (anchoredBounds.Top + anchoredBounds.Height / 2d - stationDisplay.Work.Top)) <= .5,
    "中转站沿边位置没有保存到现有 WidgetPosition。");
Check(DisplayPlacementService.GetDisplay("missing-display").Device == DisplayPlacementService.GetDisplay().Device,
    "缺失显示器没有回退到主显示器。");

var gridDisplay = new DisplayInfo(
    "test-grid",
    new NativeMethods.RECT { Left = 0, Top = 0, Right = 960, Bottom = 960 },
    new NativeMethods.RECT { Left = 0, Top = 0, Right = 960, Bottom = 960 },
    1);
var gridSnapshot = new DesktopGridSnapshot(gridDisplay, 96, 96, [], true);
DesktopGridPlacement smallPlacement = DesktopGridService.Find(gridSnapshot, [], null, 1.2)!;
DesktopGridPlacement defaultPlacement = DesktopGridService.Find(gridSnapshot, [], null, 1.56)!;
DesktopGridPlacement maximumPlacement = DesktopGridService.Find(gridSnapshot, [], null, 1.8)!;
Check(smallPlacement.CompactScale == 1.2 && defaultPlacement.CompactScale == 1.56 && maximumPlacement.CompactScale == 1.8,
    "定位网格没有使用请求的入口比例。");
Check(smallPlacement.Bounds.Width < defaultPlacement.Bounds.Width &&
      defaultPlacement.Bounds.Width < maximumPlacement.Bounds.Width &&
      smallPlacement.Bounds.Height < defaultPlacement.Bounds.Height &&
      defaultPlacement.Bounds.Height < maximumPlacement.Bounds.Height,
    "定位入口尺寸没有随比例递增。");
Check(maximumPlacement.Bounds.Width <= gridSnapshot.CellWidthPx &&
      maximumPlacement.Bounds.Height <= gridSnapshot.CellHeightPx,
    "定位入口超过了一个桌面网格。");
var tightGridSnapshot = new DesktopGridSnapshot(gridDisplay, 64, 64, [], true);
DesktopGridPlacement tightPlacement = DesktopGridService.Find(tightGridSnapshot, [], null, 1.2)!;
Check(tightPlacement.CompactScale < 1.2 &&
      tightPlacement.Bounds.Width <= tightGridSnapshot.CellWidthPx &&
      tightPlacement.Bounds.Height <= tightGridSnapshot.CellHeightPx,
    "极端桌面网格没有优先保持单格占用。");

Directory.Delete(logicRoot, recursive: true);
Console.WriteLine("TuckPane logic checks: PASS");
