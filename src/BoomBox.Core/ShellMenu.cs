using Microsoft.Win32;

namespace BoomBox.Core;

/// <summary>The "Open in BoomBox" Explorer entry. Per-user, so no administrator rights.</summary>
public static class ShellMenu
{
    private const string Label = "Open in BoomBox";

    private static readonly (string Key, string Argument)[] Targets =
    [
        (@"Software\Classes\*\shell\BoomBox", "%1"),
        (@"Software\Classes\Directory\shell\BoomBox", "%1"),
        (@"Software\Classes\Directory\Background\shell\BoomBox", "%V"),
    ];

    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(Targets[0].Key);
            return key != null;
        }
    }

    public static void Install(string exePath)
    {
        foreach (var (key, argument) in Targets)
        {
            using var shell = Registry.CurrentUser.CreateSubKey(key);
            shell.SetValue("", Label);
            shell.SetValue("Icon", exePath);
            using var command = shell.CreateSubKey("command");
            command.SetValue("", $"\"{exePath}\" open \"{argument}\"");
        }
    }

    public static void Uninstall()
    {
        foreach (var (key, _) in Targets) Registry.CurrentUser.DeleteSubKeyTree(key, false);
    }
}
