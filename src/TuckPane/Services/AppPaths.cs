namespace TuckPane.Services;

using TuckPane.Models;

public static class AppPaths
{
    private const string ProductDirectoryName = "TuckPane";
    private const string LegacyProductDirectoryName = "GlassFolder";
    private static readonly string? TestRoot = Environment.GetEnvironmentVariable("TUCKPANE_TEST_ROOT");
    private static readonly (string UserRoot, string LocalRoot) SelectedRoots = SelectRoots();

    public static string UserRoot { get; } = SelectedRoots.UserRoot;
    public static string ItemsRoot { get; } = Path.Combine(UserRoot, "Items");
    public static string WindowsRoot { get; } = Path.Combine(UserRoot, "Windows");
    public static string LocalRoot { get; } = SelectedRoots.LocalRoot;
    public static string IconCacheRoot { get; } = Path.Combine(LocalRoot, "icon-cache");
    public static string StatePath { get; } = Path.Combine(LocalRoot, "state.json");
    public static string BackupStatePath { get; } = Path.Combine(LocalRoot, "state.json.bak");
    public static string LogPath { get; } = Path.Combine(LocalRoot, "TuckPane.log");

    internal static bool IsTestMode => TestRoot is not null;

    internal static bool UsesLegacyRoots { get; } = Path.GetFileName(UserRoot)
        .Equals(LegacyProductDirectoryName, StringComparison.OrdinalIgnoreCase);

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(ItemsRoot);
        Directory.CreateDirectory(WindowsRoot);
        Directory.CreateDirectory(LocalRoot);
        Directory.CreateDirectory(IconCacheRoot);
    }

    public static string ResolveStoragePath(string relativePath)
    {
        string normalized = string.IsNullOrWhiteSpace(relativePath) ? "Items" : relativePath;
        string fullPath = Path.GetFullPath(Path.Combine(UserRoot, normalized));
        string root = Path.GetFullPath(UserRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(AppStrings.Get("StorageOutsideRoot"));
        }
        return fullPath;
    }

    public static string ResolveStoragePath(OrganizerDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.StorageAbsolutePath))
        {
            string path = definition.StorageAbsolutePath.Trim();
            if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException(AppStrings.Get("StorageAbsoluteRequired"));
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }
        return ResolveStoragePath(definition.StorageRelativePath);
    }

    public static string CreateStorageRelativePath(string name, Guid id)
    {
        return Path.Combine("Windows", CreateOwnedContainerName(name, id), "Items");
    }

    public static string CreateStorageAbsolutePath(string parentPath, string name, Guid id)
    {
        if (string.IsNullOrWhiteSpace(parentPath) || !Path.IsPathFullyQualified(parentPath))
            throw new InvalidOperationException(AppStrings.Get("StorageAbsoluteRequired"));
        return Path.Combine(Path.GetFullPath(parentPath), CreateOwnedContainerName(name, id), "Items");
    }

    public static string? GetOwnedStorageContainer(OrganizerDefinition definition)
    {
        string itemsPath;
        try { itemsPath = ResolveStoragePath(definition); }
        catch { return null; }
        if (!Path.GetFileName(itemsPath).Equals("Items", StringComparison.OrdinalIgnoreCase)) return null;
        string? container = Path.GetDirectoryName(itemsPath);
        if (container is null) return null;
        string suffix = "-" + definition.Id.ToString("N")[..8];
        return Path.GetFileName(container).EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? container : null;
    }

    private static string CreateOwnedContainerName(string name, Guid id)
    {
        string safeName = string.Concat((string.IsNullOrWhiteSpace(name) ? AppStrings.DefaultOrganizerName : name.Trim())
            .Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
        if (safeName.Length > 36) safeName = safeName[..36];
        return $"{safeName}-{id.ToString("N")[..8]}";
    }

    private static (string UserRoot, string LocalRoot) SelectRoots()
    {
        string userProfile = TestRoot is null
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.Combine(Path.GetFullPath(TestRoot), "UserProfile");
        string localAppData = TestRoot is null
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Path.Combine(Path.GetFullPath(TestRoot), "LocalAppData");

        string userRoot = Path.Combine(userProfile, ProductDirectoryName);
        string localRoot = Path.Combine(localAppData, ProductDirectoryName);
        if (Directory.Exists(userRoot) || Directory.Exists(localRoot)) return (userRoot, localRoot);

        string legacyUserRoot = Path.Combine(userProfile, LegacyProductDirectoryName);
        string legacyLocalRoot = Path.Combine(localAppData, LegacyProductDirectoryName);
        return Directory.Exists(legacyUserRoot) || Directory.Exists(legacyLocalRoot)
            ? (legacyUserRoot, legacyLocalRoot)
            : (userRoot, localRoot);
    }
}
