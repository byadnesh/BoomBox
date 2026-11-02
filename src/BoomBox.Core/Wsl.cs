using System.Diagnostics;
using System.Text;

namespace BoomBox.Core;

public sealed class BoomBoxException(string message) : Exception(message);

public sealed record WslResult(int ExitCode, string Output, string Error)
{
    public string Message => (Output + "\n" + Error).Trim();
}

/// <summary>Thin wrapper over wsl.exe.</summary>
public static class Wsl
{
    public static ProcessStartInfo Info(params string[] args)
    {
        var info = new ProcessStartInfo("wsl.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // Without this wsl.exe reports in UTF-16.
        info.Environment["WSL_UTF8"] = "1";
        foreach (var arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    public static async Task<WslResult> RunAsync(string[] args, string? stdin = null, CancellationToken ct = default)
    {
        using var process = Process.Start(Info(args)) ?? throw new BoomBoxException("Could not start wsl.exe.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        if (stdin != null)
        {
            await process.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(stdin), ct);
        }
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw;
        }
        return new WslResult(process.ExitCode, await output, await error);
    }

    public static Task<WslResult> RootShellAsync(string distro, string script, CancellationToken ct = default) =>
        RunAsync(["-d", distro, "-u", "root", "-e", "sh", "-s"], script.ReplaceLineEndings("\n"), ct);

    /// <summary>Installed distros and their state, e.g. "Running".</summary>
    public static async Task<Dictionary<string, string>> ListAsync()
    {
        var result = await RunAsync(["--list", "--verbose"]);
        var distros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (result.ExitCode != 0) return distros;

        foreach (var line in result.Output.Split('\n').Skip(1))
        {
            var parts = line.Replace("*", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3) distros[parts[0]] = parts[1];
        }
        return distros;
    }

    public static async Task EnsureAvailableAsync()
    {
        WslResult result;
        try
        {
            result = await RunAsync(["--status"]);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new BoomBoxException("WSL is not installed. Open PowerShell as administrator, run: wsl --install, then restart your PC.");
        }
        if (result.ExitCode != 0)
        {
            throw new BoomBoxException("WSL is not ready. Open PowerShell as administrator, run: wsl --install, then restart your PC.");
        }
    }
}
