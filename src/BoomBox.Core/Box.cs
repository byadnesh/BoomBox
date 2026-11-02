using System.Text.Json;

namespace BoomBox.Core;

public sealed record Box(string Folder, string Distro, string OsName, string User)
{
    public const string MetaDirName = ".boombox";

    public string MetaDir => Path.Combine(Folder, MetaDirName);
    public string Name => Path.GetFileName(Folder.TrimEnd('\\'));
}

/// <summary>
/// Each box describes itself in its own folder. The list in AppData only remembers
/// which folders to look at.
/// </summary>
public static class BoxStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoomBox");

    private static string IndexPath => Path.Combine(AppDataDir, "boxes.json");

    public static void Add(Box box)
    {
        File.WriteAllText(MetaPath(box.Folder), JsonSerializer.Serialize(box, Json));
        var folders = ReadIndex();
        if (!folders.Contains(box.Folder, StringComparer.OrdinalIgnoreCase)) folders.Add(box.Folder);
        WriteIndex(folders);
    }

    public static void Remove(Box box) =>
        WriteIndex(ReadIndex().Where(f => !f.Equals(box.Folder, StringComparison.OrdinalIgnoreCase)).ToList());

    public static List<Box> List() =>
        ReadIndex().Select(Load).OfType<Box>().ToList();

    /// <summary>Finds the box a path belongs to by walking up to its .boombox folder.</summary>
    public static Box? FindContaining(string path)
    {
        for (var dir = new DirectoryInfo(path); dir != null; dir = dir.Parent)
        {
            if (Load(dir.FullName) is { } box) return box;
        }
        return null;
    }

    private static Box? Load(string folder)
    {
        var path = MetaPath(folder);
        return File.Exists(path) ? JsonSerializer.Deserialize<Box>(File.ReadAllText(path)) : null;
    }

    private static string MetaPath(string folder) => Path.Combine(folder, Box.MetaDirName, "box.json");

    private static List<string> ReadIndex() =>
        File.Exists(IndexPath)
            ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(IndexPath)) ?? []
            : [];

    private static void WriteIndex(List<string> folders)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(folders, Json));
    }
}
