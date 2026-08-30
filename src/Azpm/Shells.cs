using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Azpm;

public enum ShellKind { Pwsh, PowerShell, Cmd, Bash, Zsh, Fish }

/// <summary>What to spawn for <c>azpm shell</c>, and any temp files to delete afterwards.</summary>
public sealed record ShellLaunch(ProcessStartInfo StartInfo, IReadOnlyList<string> TempPaths);

public static partial class Shells
{
    public static ShellKind Detect(string? explicitName)
    {
        if (!string.IsNullOrWhiteSpace(explicitName))
            return Parse(explicitName);

        if (FromName(ParentProcessName()) is { } byParent)
            return byParent;

        var shellEnv = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shellEnv) &&
            FromName(Path.GetFileNameWithoutExtension(shellEnv)) is { } byEnv)
            return byEnv;

        if (OperatingSystem.IsWindows())
            return CommandResolver.Resolve("pwsh") is not null ? ShellKind.Pwsh : ShellKind.PowerShell;
        return ShellKind.Bash;
    }

    public static ShellKind Parse(string name) => FromName(name)
        ?? throw new AzpmException(ExitCode.UsageError,
            $"unknown shell '{name}' (expected: pwsh, powershell, cmd, bash, zsh, fish)");

    private static ShellKind? FromName(string? name) => name?.ToLowerInvariant() switch
    {
        "pwsh" => ShellKind.Pwsh,
        "powershell" => ShellKind.PowerShell,
        "cmd" => ShellKind.Cmd,
        "bash" => ShellKind.Bash,
        "zsh" => ShellKind.Zsh,
        "fish" => ShellKind.Fish,
        _ => null,
    };

    /// <summary>Builds the spawn spec for an interactive subshell with the profile's prompt marker.</summary>
    public static ShellLaunch Build(ShellKind kind, string profileName)
    {
        var tag = $"[azpm:{profileName}] ";
        var temps = new List<string>();
        ProcessStartInfo psi;

        switch (kind)
        {
            case ShellKind.Pwsh or ShellKind.PowerShell:
                psi = new ProcessStartInfo(kind == ShellKind.Pwsh ? "pwsh" : "powershell");
                psi.ArgumentList.Add("-NoLogo");
                psi.ArgumentList.Add("-NoExit");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(
                    $"$__azpm=$function:prompt; function prompt {{ '{tag}' + (& $__azpm) }}");
                break;

            case ShellKind.Cmd:
                psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe");
                psi.ArgumentList.Add("/k");
                psi.ArgumentList.Add($"prompt {tag}$P$G");
                break;

            case ShellKind.Bash:
            {
                var rc = WriteTemp(temps, ".bashrc", $"""
                    [ -f ~/.bashrc ] && source ~/.bashrc
                    PS1="{tag}$PS1"
                    """);
                psi = new ProcessStartInfo("bash");
                psi.ArgumentList.Add("--rcfile");
                psi.ArgumentList.Add(rc);
                psi.ArgumentList.Add("-i");
                break;
            }

            case ShellKind.Zsh:
            {
                var dir = Path.Combine(Path.GetTempPath(), "azpm-zsh-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                temps.Add(dir);
                var orig = Environment.GetEnvironmentVariable("ZDOTDIR");
                orig = string.IsNullOrEmpty(orig) ? "$HOME" : $"\"{orig}\"";
                File.WriteAllText(Path.Combine(dir, ".zshrc"), $"""
                    [ -f {orig}/.zshrc ] && source {orig}/.zshrc
                    PROMPT="{tag}$PROMPT"
                    """);
                psi = new ProcessStartInfo("zsh") { Environment = { ["ZDOTDIR"] = dir } };
                psi.ArgumentList.Add("-i");
                break;
            }

            case ShellKind.Fish:
                psi = new ProcessStartInfo("fish");
                psi.ArgumentList.Add("-C");
                psi.ArgumentList.Add(
                    $"functions -q fish_prompt; and functions --copy fish_prompt __azpm_prompt; " +
                    $"function fish_prompt; printf '{tag}'; __azpm_prompt; end");
                break;

            default:
                throw new AzpmException(ExitCode.UsageError, $"unsupported shell: {kind}");
        }

        psi.UseShellExecute = false;
        return new ShellLaunch(psi, temps);
    }

    /// <summary>
    /// Resolves the shell executable against PATH (+ PATHEXT on Windows), since
    /// <c>Process.Start</c> with <c>UseShellExecute=false</c> doesn't.
    /// </summary>
    public static void ResolveExecutable(ProcessStartInfo psi)
    {
        if (Path.IsPathRooted(psi.FileName))
            return;
        psi.FileName = CommandResolver.Resolve(psi.FileName)
            ?? throw new AzpmException(ExitCode.UsageError, $"shell '{psi.FileName}' not found on PATH");
    }

    private static string WriteTemp(List<string> temps, string suffix, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "azpm-" + Guid.NewGuid().ToString("N") + suffix);
        File.WriteAllText(path, content);
        temps.Add(path);
        return path;
    }

    private static string? ParentProcessName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var info = new ProcessBasicInformation();
                if (NtQueryInformationProcess(Process.GetCurrentProcess().Handle, 0, ref info,
                        Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0)
                    return null;
                return Process.GetProcessById((int)info.InheritedFromUniqueProcessId).ProcessName;
            }

            var statusPpid = File.ReadLines("/proc/self/status")
                .FirstOrDefault(l => l.StartsWith("PPid:", StringComparison.Ordinal));
            if (statusPpid is null) return null;
            var ppid = int.Parse(statusPpid.AsSpan("PPid:".Length).Trim());
            return Process.GetProcessById(ppid).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint Reserved1;
        public nint PebBaseAddress;
        public nint Reserved2_0;
        public nint Reserved2_1;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(
        nint processHandle, int processInformationClass,
        ref ProcessBasicInformation processInformation, int processInformationLength,
        out int returnLength);
}
