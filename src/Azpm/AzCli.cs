using System.Diagnostics;

namespace Azpm;

/// <summary>
/// Locates and runs the real Azure CLI.
/// <para>
/// Spike S1 finding: on Windows <c>az</c> is <c>az.cmd</c>, and invoking that batch wrapper with
/// arguments containing <c>()</c> or <c>@</c> (e.g. <c>--query "length(@)"</c>) breaks cmd
/// parsing. So we resolve the Python entrypoint next to it and run that directly:
/// <c>&lt;CLI2&gt;\python.exe -I -B -m azure.cli</c>. Everywhere else <c>az</c> is directly executable.
/// </para>
/// </summary>
public sealed class AzCli : IAzRunner
{
    private readonly string _exe;
    private readonly IReadOnlyList<string> _prefixArgs;

    private AzCli(string exe, IReadOnlyList<string> prefixArgs)
    {
        _exe = exe;
        _prefixArgs = prefixArgs;
    }

    public static AzCli Locate()
    {
        var found = FindOnPath(OperatingSystem.IsWindows() ? ["az.cmd", "az.bat", "az.exe", "az"] : ["az"])
            ?? throw new AzpmException(ExitCode.AzNotFound,
                "Azure CLI ('az') not found on PATH. Install it: https://aka.ms/azcli");

        if (OperatingSystem.IsWindows() &&
            found.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            // <CLI2>\wbin\az.cmd  ->  <CLI2>\python.exe
            var wbin = Path.GetDirectoryName(found)!;
            var python = Path.Combine(Path.GetDirectoryName(wbin)!, "python.exe");
            if (File.Exists(python))
                return new AzCli(python, ["-I", "-B", "-m", "azure.cli"]);
        }

        return new AzCli(found, []);
    }

    public int Run(string configDir, IReadOnlyList<string> args)
    {
        var psi = BaseStartInfo(configDir, args);
        using var process = Process.Start(psi)
            ?? throw new AzpmException(ExitCode.AzFailed, "failed to start 'az'");
        process.WaitForExit();
        return process.ExitCode;
    }

    public AzResult Capture(string configDir, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var psi = BaseStartInfo(configDir, args);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;

        using var process = Process.Start(psi)
            ?? throw new AzpmException(ExitCode.AzFailed, "failed to start 'az'");

        var stdout = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            return new AzResult(-1, "", TimedOut: true);
        }
        process.WaitForExit(); // let the async readers drain
        return new AzResult(process.ExitCode, stdout.ToString(), TimedOut: false);
    }

    private ProcessStartInfo BaseStartInfo(string configDir, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(_exe) { UseShellExecute = false };
        foreach (var a in _prefixArgs) psi.ArgumentList.Add(a);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["AZURE_CONFIG_DIR"] = configDir;
        return psi;
    }

    private static string? FindOnPath(string[] candidates)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in candidates)
            {
                var full = Path.Combine(dir, candidate);
                if (File.Exists(full))
                    return full;
            }
        }
        return null;
    }
}
