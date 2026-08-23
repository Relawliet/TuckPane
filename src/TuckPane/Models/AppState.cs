namespace TuckPane.Models;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

public enum GlassTheme
{
    Light = 0,
    Gray = 1,
    SolidLight = 2,
    SolidDark = 3
}

public enum OrganizerLayoutMode
{
    Grid,
    Row,
    Column
}

public enum OrganizerPlacementMode
{
    Floating = 0,
    Positioned = 1
}

public enum AppLanguage
{
    ChineseSimplified = 0,
    English = 1,
    Japanese = 2
}

[Flags]
internal enum OrganizerVisualChange
{
    None = 0,
    Name = 1 << 0,
    Theme = 1 << 1,
    Layout = 1 << 2,
    CompactScale = 1 << 3,
    CanvasScale = 1 << 4,
    ItemScale = 1 << 5,
    NameScale = 1 << 6,
    PlacementMode = 1 << 7,
    PositionLock = 1 << 8,
    All = Name | Theme | Layout | CompactScale | CanvasScale | ItemScale | NameScale | PlacementMode | PositionLock
}

public sealed class AppStateV2
{
    public int SchemaVersion { get; set; } = 2;
    public GlobalSettings GlobalSettings { get; set; } = new();
    public ConsolePlacement? ConsolePlacement { get; set; }
    public List<OrganizerDefinition> Organizers { get; set; } = [];
}

public sealed class GlobalSettings
{
    public GlassTheme Theme { get; set; } = GlassTheme.Light;
    public bool StartWithWindows { get; set; }
    public AppLanguage Language { get; set; } = AppLanguage.ChineseSimplified;
}

public sealed class ConsolePlacement
{
    public double XDip { get; set; }
    public double YDip { get; set; }
    public double WidthDip { get; set; } = 960;
    public double HeightDip { get; set; } = 680;
    public string MonitorDevice { get; set; } = string.Empty;
}

public sealed class OrganizerDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "收纳窗";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public GlassTheme? ThemeOverride { get; set; }
    public OrganizerPlacementMode PlacementMode { get; set; } = OrganizerPlacementMode.Floating;
    public bool PositionLocked { get; set; }
    public OrganizerLayout Layout { get; set; } = new();
    public double CompactScale { get; set; } = OrganizerLimits.MinimumCompactScale;
    public double CanvasScale { get; set; } = 1;
    public double ItemScale { get; set; } = 1;
    public double NameScale { get; set; } = 1;
    public double? ManualCanvasBaseWidthDip { get; set; }
    public double? ManualCanvasBaseHeightDip { get; set; }
    public WidgetPosition? Position { get; set; }
    public string StorageRelativePath { get; set; } = string.Empty;
    public string? StorageAbsolutePath { get; set; }
    public List<string> ItemOrder { get; set; } = [];
}

public sealed class OrganizerLayout
{
    public OrganizerLayoutMode Mode { get; set; } = OrganizerLayoutMode.Grid;
    public int Rows { get; set; } = 3;
    public int Columns { get; set; } = 3;

    [JsonIgnore]
    public int VisibleItemCount => Rows * Columns;
}

// Kept only as the on-disk migration input for 0.1.x installations.
public sealed class AppStateV1
{
    public int SchemaVersion { get; set; } = 1;
    public string WidgetName { get; set; } = "文件夹";
    public bool StartWithWindows { get; set; }
    public WidgetPosition? Position { get; set; }
    public List<string> ItemOrder { get; set; } = [];
}

public sealed class WidgetPosition
{
    public string MonitorDevice { get; set; } = string.Empty;
    public double XDip { get; set; }
    public double YDip { get; set; }
    public double SavedWorkAreaWidthDip { get; set; }
    public double SavedWorkAreaHeightDip { get; set; }
}

public enum WidgetItemKind
{
    Folder,
    Shortcut,
    InternetShortcut,
    File
}

public sealed record WidgetItem : INotifyPropertyChanged
{
    private string _name;
    private string _fullPath;
    private string _relativeName;
    private WidgetItemKind _kind;

    public WidgetItem(string name, string fullPath, string relativeName, WidgetItemKind kind)
    {
        _name = name;
        _fullPath = fullPath;
        _relativeName = relativeName;
        _kind = kind;
    }

    public string Name
    {
        get => _name;
        private set => SetField(ref _name, value);
    }

    public string FullPath
    {
        get => _fullPath;
        private set => SetField(ref _fullPath, value);
    }

    public string RelativeName
    {
        get => _relativeName;
        private set => SetField(ref _relativeName, value);
    }

    public WidgetItemKind Kind
    {
        get => _kind;
        private set => SetField(ref _kind, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal bool HasSameValue(WidgetItem other) =>
        Name.Equals(other.Name, StringComparison.Ordinal) &&
        FullPath.Equals(other.FullPath, StringComparison.Ordinal) &&
        RelativeName.Equals(other.RelativeName, StringComparison.Ordinal) &&
        Kind == other.Kind;

    internal void ApplyValue(WidgetItem other)
    {
        Name = other.Name;
        FullPath = other.FullPath;
        RelativeName = other.RelativeName;
        Kind = other.Kind;
    }

    internal WidgetItem CopyValue() => new(Name, FullPath, RelativeName, Kind);

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum TransferStatus
{
    Moved,
    ShortcutCreated,
    CopiedSourceRetained,
    Cancelled,
    Failed
}

public sealed record TransferOutcome(
    string SourcePath,
    string? DestinationPath,
    TransferStatus Status,
    string Message);

public sealed record TransferProgress(string ItemName, long BytesCopied, long TotalBytes)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesCopied / TotalBytes, 0, 1);
}
