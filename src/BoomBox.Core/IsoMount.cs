using System.Diagnostics;

namespace BoomBox.Core;

/// <summary>Attaches an ISO to Windows as a drive letter for as long as the object lives.</summary>
public sealed class IsoMount : IAsyncDisposable
{
    private const string MountScript = """
        $ErrorActionPreference = 'Stop'
        $image = Get-DiskImage -ImagePath $env:BB_ISO
        $owned = -not $image.Attached
        if ($owned) { $image = Mount-DiskImage -ImagePath $env:BB_ISO -StorageType ISO -PassThru }
        $letter = ($image | Get-Volume).DriveLetter
        if (-not $letter) { throw 'Windows did not assign a drive letter to the ISO.' }
        Write-Output "$letter $owned"
        """;

    private readonly string _iso;
    private readonly bool _owned;

    public char Drive { get; }

    private IsoMount(string iso, char drive, bool owned)
    {
        _iso = iso;
        Drive = drive;
        _owned = owned;
    }

    public static async Task<IsoMount> MountAsync(string iso)
    {
        var (exitCode, output, error) = await PowerShellAsync(MountScript, iso);
        if (exitCode != 0)
        {
            throw new BoomBoxException($"Windows could not open the ISO. It may be damaged.\n{error.Trim()}");
        }
        var parts = output.Trim().Split(' ');
        return new IsoMount(iso, parts[0][0], parts[1].Equals("True", StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask DisposeAsync()
    {
        if (_owned) await PowerShellAsync("Dismount-DiskImage -ImagePath $env:BB_ISO | Out-Null", _iso);
    }

    private static async Task<(int, string, string)> PowerShellAsync(string script, string iso)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(script);
        // Passed by environment so paths need no quoting.
        info.Environment["BB_ISO"] = iso;

        using var process = Process.Start(info) ?? throw new BoomBoxException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }
}
