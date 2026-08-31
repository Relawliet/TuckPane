using System.Text.Json;
using TuckPane.Core;
using TuckPane.Models;

namespace TuckPane.Services;

public sealed class StateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly string _statePath;
    private readonly string _backupPath;

    public StateStore(string? statePath = null)
    {
        _statePath = Path.GetFullPath(statePath ?? AppPaths.StatePath);
        _backupPath = _statePath + ".bak";
    }

    public async Task<AppStateV2> LoadAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        LoadResult? loaded = await TryLoadAsync(_statePath) ?? await TryLoadAsync(_backupPath);
        AppStateV2 state = Normalize(loaded?.State ?? new AppStateV2());
        if (loaded is { RequiresMigration: true }) await PersistMigrationAsync(state, loaded.SourcePath);
        return state;
    }

    public async Task SaveAsync(AppStateV2 state)
    {
        await _saveGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            string temporary = _statePath + ".tmp";
            string json = JsonSerializer.Serialize(Normalize(state), JsonOptions);
            await File.WriteAllTextAsync(temporary, json);
            if (File.Exists(_statePath)) File.Copy(_statePath, _backupPath, overwrite: true);
            await MoveIntoPlaceAsync(temporary, _statePath);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task PersistMigrationAsync(AppStateV2 state, string legacyPath)
    {
        await _saveGate.WaitAsync();
        try
        {
            string temporary = _statePath + ".tmp";
            if (!legacyPath.Equals(_backupPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(legacyPath, _backupPath, overwrite: true);
            }
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, JsonOptions));
            await MoveIntoPlaceAsync(temporary, _statePath);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private static async Task MoveIntoPlaceAsync(string temporary, string destination)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporary, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < 9 && ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(50);
            }
        }
    }

    private static async Task<LoadResult?> TryLoadAsync(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            string json = await File.ReadAllTextAsync(path);
            using JsonDocument document = JsonDocument.Parse(json);
            int schemaVersion = document.RootElement.TryGetProperty("SchemaVersion", out JsonElement schema)
                ? schema.GetInt32()
                : 1;
            if (schemaVersion >= 2)
            {
                AppStateV2? current = JsonSerializer.Deserialize<AppStateV2>(json, JsonOptions);
                if (current is null) return null;
                current.GlobalSettings ??= new GlobalSettings();
                if (schemaVersion < 3) current.GlobalSettings.Language = AppLanguage.ChineseSimplified;
                return new(current, schemaVersion < 5, path);
            }

            AppStateV1 legacy = JsonSerializer.Deserialize<AppStateV1>(json, JsonOptions) ?? new AppStateV1();
            return new(Migrate(legacy, File.GetCreationTimeUtc(path)), true, path);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"无法读取状态文件：{path}", ex);
            return null;
        }
    }

    private sealed record LoadResult(AppStateV2 State, bool RequiresMigration, string SourcePath);

    internal static AppStateV2 Migrate(AppStateV1 legacy, DateTime createdAtUtc)
    {
        var organizer = new OrganizerDefinition
        {
            Name = string.IsNullOrWhiteSpace(legacy.WidgetName) ? "文件夹" : legacy.WidgetName.Trim(),
            CreatedAtUtc = createdAtUtc == default ? DateTimeOffset.UtcNow : createdAtUtc,
            StorageRelativePath = "Items",
            Position = legacy.Position,
            ItemOrder = legacy.ItemOrder.ToList(),
            Layout = new OrganizerLayout { Mode = OrganizerLayoutMode.Grid, Rows = 3, Columns = 3 }
        };
        return new AppStateV2
        {
            GlobalSettings = new GlobalSettings
            {
                Theme = GlassTheme.Light,
                StartWithWindows = legacy.StartWithWindows
            },
            Organizers = [organizer]
        };
    }

    internal static AppStateV2 Normalize(AppStateV2 state)
    {
        state.SchemaVersion = 5;
        state.GlobalSettings ??= new GlobalSettings();
        if (!Enum.IsDefined(state.GlobalSettings.Theme)) state.GlobalSettings.Theme = GlassTheme.Light;
        if (!Enum.IsDefined(state.GlobalSettings.Language)) state.GlobalSettings.Language = AppLanguage.ChineseSimplified;
        state.Organizers ??= [];
        var normalizedOrganizers = new List<OrganizerDefinition>();
        var stationEdges = new HashSet<OrganizerDockEdge>();
        int regularCount = 0;
        foreach (OrganizerDefinition organizer in state.Organizers)
        {
            if (!Enum.IsDefined(organizer.PlacementMode)) organizer.PlacementMode = OrganizerPlacementMode.Floating;
            if (!Enum.IsDefined(organizer.DockEdge)) organizer.DockEdge = OrganizerDockEdge.Right;
            if (organizer.PlacementMode == OrganizerPlacementMode.Station && !stationEdges.Add(organizer.DockEdge))
                organizer.PlacementMode = OrganizerPlacementMode.Floating;
            if (organizer.PlacementMode == OrganizerPlacementMode.Station || regularCount++ < OrganizerLimits.MaximumOrganizers)
                normalizedOrganizers.Add(organizer);
        }
        state.Organizers = normalizedOrganizers;

        var ids = new HashSet<Guid>();
        var noteIds = new HashSet<Guid>();
        foreach (OrganizerDefinition organizer in state.Organizers)
        {
            if (organizer.Id == Guid.Empty || !ids.Add(organizer.Id))
            {
                organizer.Id = Guid.NewGuid();
                ids.Add(organizer.Id);
            }
            organizer.Name = string.IsNullOrWhiteSpace(organizer.Name) ? "收纳窗" : organizer.Name.Trim();
            if (organizer.CreatedAtUtc == default) organizer.CreatedAtUtc = DateTimeOffset.UtcNow;
            if (organizer.ThemeOverride is GlassTheme theme && !Enum.IsDefined(theme)) organizer.ThemeOverride = null;
            if (!Enum.IsDefined(organizer.PlacementMode)) organizer.PlacementMode = OrganizerPlacementMode.Floating;
            if (!Enum.IsDefined(organizer.DockEdge)) organizer.DockEdge = OrganizerDockEdge.Right;
            organizer.Layout ??= new OrganizerLayout();
            bool station = organizer.PlacementMode == OrganizerPlacementMode.Station;
            if (organizer.Layout.Mode != OrganizerLayoutMode.Grid)
            {
                organizer.Layout.Mode = OrganizerLayoutMode.Grid;
                organizer.Layout.Rows = 3;
                organizer.Layout.Columns = 3;
            }
            else
            {
                organizer.Layout.Rows = Math.Clamp(
                    organizer.Layout.Rows,
                    station ? OrganizerLimits.MinimumStationRows : OrganizerLimits.MinimumGridDimension,
                    station ? OrganizerLimits.MaximumStationRows : OrganizerLimits.MaximumLayoutDimension);
                organizer.Layout.Columns = Math.Clamp(
                    organizer.Layout.Columns,
                    station ? OrganizerLimits.MinimumStationColumns : OrganizerLimits.MinimumGridDimension,
                    station ? OrganizerLimits.MaximumStationColumns : OrganizerLimits.MaximumLayoutDimension);
            }
            double maximumCompactScale = organizer.PlacementMode == OrganizerPlacementMode.Positioned
                ? OrganizerLimits.MaximumPositionedCompactScale
                : OrganizerLimits.MaximumCompactScale;
            organizer.CompactScale = Math.Clamp(
                organizer.CompactScale,
                OrganizerLimits.MinimumCompactScale,
                maximumCompactScale);
            organizer.CanvasScale = Math.Clamp(organizer.CanvasScale, .1, 1.2);
            organizer.ItemScale = Math.Clamp(organizer.ItemScale, .5, 1.65);
            organizer.NameScale = Math.Clamp(organizer.NameScale, .6, 1);
            if (station)
            {
                organizer.ManualCanvasBaseWidthDip = null;
                organizer.ManualCanvasBaseHeightDip = null;
            }
            else if (organizer.ManualCanvasBaseWidthDip is not double baseWidth ||
                organizer.ManualCanvasBaseHeightDip is not double baseHeight ||
                !double.IsFinite(baseWidth) || !double.IsFinite(baseHeight) ||
                baseWidth <= 0 || baseHeight <= 0)
            {
                organizer.ManualCanvasBaseWidthDip = null;
                organizer.ManualCanvasBaseHeightDip = null;
            }
            else
            {
                organizer.ManualCanvasBaseWidthDip = Math.Clamp(baseWidth, 1, 10000);
                organizer.ManualCanvasBaseHeightDip = Math.Clamp(baseHeight, 1, 10000);
            }
            if (!string.IsNullOrWhiteSpace(organizer.StorageAbsolutePath))
            {
                string absolute = organizer.StorageAbsolutePath.Trim();
                organizer.StorageAbsolutePath = Path.IsPathFullyQualified(absolute) ? Path.GetFullPath(absolute) : absolute;
                organizer.StorageRelativePath = string.Empty;
            }
            else
            {
                organizer.StorageAbsolutePath = null;
                organizer.StorageRelativePath = string.IsNullOrWhiteSpace(organizer.StorageRelativePath)
                    ? AppPaths.CreateStorageRelativePath(organizer.Name, organizer.Id)
                    : organizer.StorageRelativePath;
                try
                {
                    _ = AppPaths.ResolveStoragePath(organizer.StorageRelativePath);
                }
                catch
                {
                    organizer.StorageRelativePath = AppPaths.CreateStorageRelativePath(organizer.Name, organizer.Id);
                }
            }
            organizer.Notes ??= [];
            var noteNames = new List<string>();
            foreach (NoteDefinition note in organizer.Notes)
            {
                if (note.Id == Guid.Empty || !noteIds.Add(note.Id))
                {
                    note.Id = Guid.NewGuid();
                    noteIds.Add(note.Id);
                }
                string name = note.Name?.Trim() ?? string.Empty;
                note.Name = string.IsNullOrWhiteSpace(name) ||
                    noteNames.Contains(name, StringComparer.CurrentCultureIgnoreCase)
                    ? OrganizerNoteRules.CreateDefaultName(noteNames)
                    : name;
                noteNames.Add(note.Name);
                if (!Enum.IsDefined(note.Theme)) note.Theme = NoteTheme.RainBlue;
                note.FontSize = double.IsFinite(note.FontSize)
                    ? Math.Clamp(note.FontSize, OrganizerNoteRules.MinimumFontSize, OrganizerNoteRules.MaximumFontSize)
                    : 14;
                if (note.Placement is { } placement)
                {
                    if (!double.IsFinite(placement.XDip) || !double.IsFinite(placement.YDip) ||
                        !double.IsFinite(placement.WidthDip) || !double.IsFinite(placement.HeightDip))
                    {
                        note.Placement = null;
                    }
                    else
                    {
                        placement.WidthDip = Math.Clamp(placement.WidthDip, 280, 1600);
                        placement.HeightDip = Math.Clamp(placement.HeightDip, 220, 1200);
                    }
                }
            }
            organizer.ItemOrder = (organizer.ItemOrder ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.StartsWith("note:", StringComparison.OrdinalIgnoreCase)
                    ? name
                    : Path.GetFileName(name)!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (state.ConsolePlacement is not null)
        {
            state.ConsolePlacement.WidthDip = Math.Max(860, state.ConsolePlacement.WidthDip);
            state.ConsolePlacement.HeightDip = Math.Max(600, state.ConsolePlacement.HeightDip);
        }
        return state;
    }
}
