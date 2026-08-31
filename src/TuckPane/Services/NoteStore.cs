namespace TuckPane.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using TuckPane.Core;
using TuckPane.Models;

public sealed class NoteStore
{
    internal const int MaximumHtmlLength = 64 * 1024 * 1024;
    internal const long MaximumPortableFileLength = 64L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions PortableJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root;

    public NoteStore(string? root = null) => _root = Path.GetFullPath(root ?? AppPaths.NotesRoot);

    public async Task<NoteDocument> LoadAsync(Guid noteId)
    {
        string path = GetPath(noteId);
        Exception? loadError = null;
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                await using FileStream stream = File.OpenRead(candidate);
                NoteDocument document = await JsonSerializer.DeserializeAsync<NoteDocument>(stream, JsonOptions) ?? new NoteDocument();
                document.Html ??= string.Empty;
                if (document.Html.Length > MaximumHtmlLength)
                    throw new InvalidDataException("The note document exceeds the supported size.");
                document.Version = 1;
                return document;
            }
            catch (Exception ex)
            {
                loadError = ex;
                AppLogger.Error($"无法读取便签文件：{candidate}", ex);
            }
        }
        if (loadError is not null)
            throw new InvalidDataException("The note document and its backup could not be read.", loadError);
        return new NoteDocument();
    }

    public async Task SaveAsync(Guid noteId, NoteDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Html ??= string.Empty;
        if (document.Html.Length > MaximumHtmlLength)
            throw new InvalidDataException("The note document exceeds the supported size.");
        document.Version = 1;
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_root);
            string path = GetPath(noteId);
            string temporary = path + ".tmp";
            await using (var stream = new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough
            }))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions);
                await stream.FlushAsync();
            }
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task CopyAsync(Guid sourceId, Guid destinationId) =>
        await SaveAsync(destinationId, await LoadAsync(sourceId));

    public async Task DeleteAsync(Guid noteId)
    {
        await _gate.WaitAsync();
        try
        {
            foreach (string suffix in new[] { string.Empty, ".bak", ".tmp" })
            {
                string path = GetPath(noteId) + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<bool> ExistsAsync(Guid noteId) => Task.FromResult(File.Exists(GetPath(noteId)));

    internal async Task<string> CreatePortableStagingAsync(string noteName, PortableNoteDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidatePortableDocument(document);
        string directory = Path.Combine(AppPaths.NoteStagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, CreatePortableFileName(noteName));
        string temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await WritePortableTemporaryAsync(temporary, document);
            File.Move(temporary, path);
            return path;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            throw;
        }
    }

    internal async Task<PortableNoteDocument> LoadPortableAsync(string path)
    {
        string fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(fullPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        if (stream.Length > MaximumPortableFileLength)
            throw new InvalidDataException("The portable note exceeds 64 MiB.");
        try
        {
            PortableNoteDocument document = await JsonSerializer.DeserializeAsync<PortableNoteDocument>(stream, PortableJsonOptions)
                ?? throw new InvalidDataException("The portable note is empty.");
            ValidatePortableDocument(document);
            return document;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The portable note is not valid TuckPane.Note JSON v1.", ex);
        }
    }

    internal async Task SavePortableAsync(string path, PortableNoteDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidatePortableDocument(document);
        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("The portable note has no parent directory.");
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The portable note was moved or deleted.", fullPath);
        string temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        await _gate.WaitAsync();
        try
        {
            await WritePortableTemporaryAsync(temporary, document);
            File.Replace(temporary, fullPath, destinationBackupFileName: null);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }

    internal static string CreatePortableFileName(string? noteName)
    {
        string safe = string.Concat((string.IsNullOrWhiteSpace(noteName) ? "便签" : noteName.Trim())
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character))
            .TrimEnd(' ', '.');
        if (safe.Length == 0) safe = "便签";
        if (safe.Length > 120)
        {
            int length = 120;
            if (char.IsHighSurrogate(safe[length - 1]) && char.IsLowSurrogate(safe[length])) length--;
            safe = safe[..length].TrimEnd(' ', '.');
        }
        string deviceName = safe.Split('.', 2)[0];
        if (IsReservedDeviceName(deviceName)) safe = '_' + safe;
        return safe + ".tucknote";
    }

    private static async Task WritePortableTemporaryAsync(string temporary, PortableNoteDocument document)
    {
        await using var stream = new FileStream(temporary, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough
        });
        await JsonSerializer.SerializeAsync(stream, document, PortableJsonOptions);
        await stream.FlushAsync();
        if (stream.Length > MaximumPortableFileLength)
            throw new InvalidDataException("The portable note exceeds 64 MiB.");
    }

    private static void ValidatePortableDocument(PortableNoteDocument document)
    {
        if (!string.Equals(document.Format, "TuckPane.Note", StringComparison.Ordinal))
            throw new InvalidDataException("Unknown portable note format.");
        if (document.Version != 1) throw new InvalidDataException("Unsupported portable note version.");
        if (!Enum.IsDefined(document.Theme)) throw new InvalidDataException("Unknown portable note theme.");
        if (!double.IsFinite(document.FontSize) ||
            document.FontSize < OrganizerNoteRules.MinimumFontSize ||
            document.FontSize > OrganizerNoteRules.MaximumFontSize)
            throw new InvalidDataException("The portable note font size is invalid.");
        if (document.Html is null || document.Html.Length > MaximumHtmlLength)
            throw new InvalidDataException("The portable note HTML is invalid or too large.");
        if (document.Placement is not { } placement) return;
        if (placement.MonitorDevice is null ||
            !double.IsFinite(placement.XDip) || !double.IsFinite(placement.YDip) ||
            !double.IsFinite(placement.WidthDip) || !double.IsFinite(placement.HeightDip) ||
            placement.WidthDip is < 280 or > 1600 || placement.HeightDip is < 220 or > 1200)
            throw new InvalidDataException("The portable note placement is invalid.");
    }

    internal static bool IsReservedDeviceName(string name)
    {
        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase)) return true;
        return name.Length == 4 && name[3] is >= '1' and <= '9' &&
            (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    private string GetPath(Guid noteId) => Path.Combine(_root, $"{noteId:N}.json");
}
