using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BoomBox.Core;

public sealed record BoxProgress(string Stage, double? Percent = null);

public static partial class BoxCreator
{
    private const string HelperDistro = "BoomBox-Helper";
    private const string HelperAsset = "alpine-minirootfs-3.24.3-x86_64.tar.gz";

    public static async Task<Box> CreateAsync(string folder, string iso, IProgress<BoxProgress> progress, CancellationToken ct = default)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        iso = Path.GetFullPath(iso);

        if (!File.Exists(iso)) throw new BoomBoxException("The ISO file does not exist.");
        if (!char.IsLetter(folder[0]) || folder[1] != ':')
        {
            throw new BoomBoxException("The box folder must be on a local drive, such as C:\\Boxes\\Kali.");
        }
        if (Directory.Exists(Path.Combine(folder, Box.MetaDirName)))
        {
            throw new BoomBoxException("This folder is already a box.");
        }

        progress.Report(new BoxProgress("Checking this PC"));
        await Wsl.EnsureAvailableAsync();
        await EnsureHelperAsync(progress, ct);

        var distro = await PickDistroNameAsync(Path.GetFileName(folder));
        var meta = Path.Combine(folder, Box.MetaDirName);
        Directory.CreateDirectory(meta);
        File.SetAttributes(meta, File.GetAttributes(meta) | FileAttributes.Hidden);

        try
        {
            string osName;
            progress.Report(new BoxProgress("Opening the ISO"));
            await using (var mount = await IsoMount.MountAsync(iso))
            {
                osName = await ImportAsync(distro, meta, mount.Drive, progress, ct);
            }

            progress.Report(new BoxProgress("Skipping the setup for you", 95));
            var user = await SetUpAsync(distro, folder, ct);

            var box = new Box(folder, distro, osName, user);
            WriteLaunchers(box);
            BoxStore.Add(box);
            progress.Report(new BoxProgress("Ready", 100));
            return box;
        }
        catch
        {
            // A killed import can still leave the name registered.
            if ((await Wsl.ListAsync()).ContainsKey(distro)) await Wsl.RunAsync(["--unregister", distro]);
            TryDeleteDirectory(meta);
            throw;
        }
        finally
        {
            await Wsl.RunAsync(["--terminate", HelperDistro]);
        }
    }

    public static async Task DeleteAsync(Box box)
    {
        var result = await Wsl.RunAsync(["--unregister", box.Distro]);
        if (result.ExitCode != 0)
        {
            throw new BoomBoxException($"Could not delete the box.\n{result.Message}");
        }
        foreach (var launcher in Launcher.Files) File.Delete(Path.Combine(box.Folder, launcher));
        TryDeleteDirectory(box.MetaDir);
        BoxStore.Remove(box);
    }

    private static async Task EnsureHelperAsync(IProgress<BoxProgress> progress, CancellationToken ct)
    {
        if ((await Wsl.ListAsync()).ContainsKey(HelperDistro)) return;

        progress.Report(new BoxProgress("Preparing BoomBox for first use"));
        var dir = Path.Combine(BoxStore.AppDataDir, "helper");
        Directory.CreateDirectory(dir);
        var tar = Path.Combine(dir, HelperAsset);
        await using (var file = File.Create(tar))
        await using (var resource = Resource(HelperAsset))
        {
            await resource.CopyToAsync(file, ct);
        }

        var result = await Wsl.RunAsync(["--import", HelperDistro, dir, tar, "--version", "2"], ct: ct);
        File.Delete(tar);
        if (result.ExitCode != 0)
        {
            throw new BoomBoxException($"Could not prepare the helper.\n{result.Message}");
        }
    }

    /// <summary>Streams the OS from the helper straight into wsl --import, with no temporary tar.</summary>
    private static async Task<string> ImportAsync(string distro, string target, char drive, IProgress<BoxProgress> progress, CancellationToken ct)
    {
        var script = $"DRIVE={drive}\n{ReadScript("extract.sh")}";
        using var extract = Process.Start(Wsl.Info("-d", HelperDistro, "-u", "root", "-e", "sh", "-s"))
            ?? throw new BoomBoxException("Could not start wsl.exe.");
        await extract.StandardInput.WriteAsync(script.ReplaceLineEndings("\n"));
        extract.StandardInput.Close();

        var osName = "Linux";
        long total = 0;
        string? error = null;
        using var diskFull = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, diskFull.Token);

        var status = Task.Run(async () =>
        {
            while (await extract.StandardError.ReadLineAsync() is { } line)
            {
                if (!line.StartsWith("BB:")) continue;
                var space = line.IndexOf(' ');
                var value = space < 0 ? "" : line[(space + 1)..];
                switch (space < 0 ? line : line[..space])
                {
                    case "BB:STAGE": progress.Report(new BoxProgress(value)); break;
                    case "BB:OS": osName = value; break;
                    case "BB:ERROR": error = value; break;
                    case "BB:SIZE":
                        total = long.Parse(value);
                        var free = new DriveInfo(target[..1]).AvailableFreeSpace;
                        if (free < total * 1.15)
                        {
                            error = $"Not enough disk space. This OS needs about {Gb(total * 1.15)} GB and the drive has {Gb(free)} GB free.";
                            diskFull.Cancel();
                        }
                        break;
                }
            }
        }, CancellationToken.None);

        Process? import = null;
        var buffer = new byte[1024 * 1024];
        long copied = 0;
        var reported = 0;
        try
        {
            var source = extract.StandardOutput.BaseStream;
            int read;
            while ((read = await source.ReadAsync(buffer, linked.Token)) > 0)
            {
                // Started on the first byte, so a rejected ISO never reaches wsl --import.
                import ??= Process.Start(Wsl.Info("--import", distro, target, "-", "--version", "2"))
                    ?? throw new BoomBoxException("Could not start wsl.exe.");
                await import.StandardInput.BaseStream.WriteAsync(buffer.AsMemory(0, read), linked.Token);
                copied += read;
                var percent = total > 0 ? (int)(5 + 85 * Math.Min(1.0, (double)copied / total)) : 0;
                if (percent > reported)
                {
                    reported = percent;
                    progress.Report(new BoxProgress("Copying the operating system into your folder", percent));
                }
            }
            await extract.WaitForExitAsync(linked.Token);
            await status;
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            Kill(extract);
            Kill(import);
            import?.Dispose();
            await status;
            if (diskFull.IsCancellationRequested) throw new BoomBoxException(error!);
            ct.ThrowIfCancellationRequested();
            throw new BoomBoxException(error ?? $"Copying the system failed.\n{e.Message}");
        }

        if (error != null || extract.ExitCode != 0 || import == null)
        {
            Kill(import);
            import?.Dispose();
            throw new BoomBoxException(error ?? "The helper stopped before copying anything.");
        }

        using (import)
        {
            progress.Report(new BoxProgress("Finishing the system disk", 92));
            import.StandardInput.Close();
            var importOutput = import.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var importError = import.StandardError.ReadToEndAsync(CancellationToken.None);
            await import.WaitForExitAsync(CancellationToken.None);
            if (import.ExitCode != 0)
            {
                throw new BoomBoxException($"WSL could not import the system.\n{await importOutput}{await importError}".Trim());
            }
        }
        return osName;
    }

    private static async Task<string> SetUpAsync(string distro, string folder, CancellationToken ct)
    {
        var script =
            $"BOX_HOST={ShellQuote(HostName(Path.GetFileName(folder)))}\n" +
            $"BOX_PATH={ShellQuote(folder.Replace('\\', '/'))}\n" +
            ReadScript("setup.sh");
        var result = await Wsl.RootShellAsync(distro, script, ct);

        var user = result.Output.Split('\n')
            .FirstOrDefault(l => l.StartsWith("BB:USER "))?["BB:USER ".Length..].Trim();
        if (user == null)
        {
            throw new BoomBoxException($"The new system could not be set up.\n{result.Message}");
        }

        // wsl.conf is only read when a distro starts.
        await Wsl.RunAsync(["--terminate", distro], ct: ct);
        return user;
    }

    private static void WriteLaunchers(Box box)
    {
        File.WriteAllText(Path.Combine(box.Folder, Launcher.TerminalFile),
            $"@echo off\r\nwsl.exe -d {box.Distro} --cd /box\r\n");
        File.WriteAllText(Path.Combine(box.Folder, Launcher.SystemFilesFile),
            $"@echo off\r\nstart \"\" explorer.exe \"{Launcher.SystemFilesPath(box)}\"\r\n");
    }

    private static async Task<string> PickDistroNameAsync(string folderName)
    {
        var installed = await Wsl.ListAsync();
        var slug = InvalidNameChars().Replace(folderName, "-").Trim('-');
        var name = $"BoomBox-{(slug.Length == 0 ? "Box" : slug)}";
        var candidate = name;
        for (var n = 2; installed.ContainsKey(candidate); n++) candidate = $"{name}-{n}";
        return candidate;
    }

    private static string HostName(string folderName)
    {
        var host = InvalidHostChars().Replace(folderName.ToLowerInvariant(), "-").Trim('-');
        host = host.Length > 32 ? host[..32].TrimEnd('-') : host;
        return host.Length == 0 ? "boombox" : host;
    }

    private static string ShellQuote(string value) => $"'{value.Replace("'", "'\\''")}'";

    private static string Gb(double bytes) => (bytes / (1024 * 1024 * 1024)).ToString("0.0");

    private static void Kill(Process? process)
    {
        try
        {
            if (process is { HasExited: false }) process.Kill(true);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException)
        {
            // WSL can hold the disk file a moment longer; nothing depends on it now.
        }
    }

    private static Stream Resource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(fileName, StringComparison.Ordinal));
        return assembly.GetManifestResourceStream(name)!;
    }

    private static string ReadScript(string fileName)
    {
        using var reader = new StreamReader(Resource(fileName));
        return reader.ReadToEnd();
    }

    [GeneratedRegex("[^A-Za-z0-9._-]+")]
    private static partial Regex InvalidNameChars();

    [GeneratedRegex("[^a-z0-9-]+")]
    private static partial Regex InvalidHostChars();
}
