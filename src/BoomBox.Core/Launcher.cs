using System.Diagnostics;

namespace BoomBox.Core;

/// <summary>Opens things inside a box.</summary>
public static class Launcher
{
    public const string TerminalFile = "Terminal.cmd";
    public const string SystemFilesFile = "System Files.cmd";

    public static readonly string[] Files = [TerminalFile, SystemFilesFile];

    public static string SystemFilesPath(Box box) => $@"\\wsl.localhost\{box.Distro}\home\{box.User}";

    public static void OpenTerminal(Box box, string? windowsDirectory = null)
    {
        Start("-d", box.Distro, "--cd", ToBoxPath(box, windowsDirectory ?? box.Folder));
    }

    public static void OpenSystemFiles(Box box) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SystemFilesPath(box)}\"") { UseShellExecute = true });

    public static void OpenFolder(Box box) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{box.Folder}\"") { UseShellExecute = true });

    /// <summary>Opens a file or folder from a box folder inside that box's OS.</summary>
    public static void OpenPath(string path)
    {
        path = Path.GetFullPath(path);
        var box = BoxStore.FindContaining(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!)
            ?? throw new BoomBoxException($"This is not inside a box:\n{path}");

        if (Directory.Exists(path))
        {
            OpenTerminal(box, path);
            return;
        }
        Start("-d", box.Distro, "--cd", ToBoxPath(box, Path.GetDirectoryName(path)!),
            "-e", "/usr/local/bin/boombox-open", Path.GetFileName(path));
    }

    public static string ToBoxPath(Box box, string windowsPath)
    {
        var relative = Path.GetRelativePath(box.Folder, windowsPath);
        return relative == "." ? "/box" : "/box/" + relative.Replace('\\', '/');
    }

    private static void Start(params string[] args)
    {
        var info = new ProcessStartInfo("wsl.exe") { UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        Process.Start(info)?.Dispose();
    }
}
