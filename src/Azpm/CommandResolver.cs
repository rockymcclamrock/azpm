using System.Diagnostics;
using System.Text;

namespace Azpm;

/// <summary>
/// Resolves a command name to something <see cref="Process"/> can start, searching <c>PATH</c>
/// (and <c>PATHEXT</c> on Windows). Batch files (<c>.cmd</c>/<c>.bat</c>) can't be executed
/// directly, so they're wrapped in <c>%ComSpec% /d /s /c "…"</c>.
/// </summary>
public static class CommandResolver
{
    public static ProcessStartInfo BuildStartInfo(IReadOnlyList<string> command)
    {
        var resolved = Resolve(command[0])
            ?? throw new AzpmException(ExitCode.AzFailed, $"command not found on PATH: '{command[0]}'");

        var rest = command.Skip(1);

        if (OperatingSystem.IsWindows() && IsBatch(resolved))
        {
            // cmd.exe can't be handed a batch path + args through ArgumentList without its
            // quote-stripping mangling spaces in the path. Build the command line by hand:
            // with /s, cmd removes exactly the outer quotes and runs the rest verbatim.
            var line = new StringBuilder();
            line.Append(CmdQuote(resolved));
            foreach (var arg in rest)
                line.Append(' ').Append(CmdQuote(arg));

            var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
            {
                Arguments = $"/d /s /c \"{line}\"",
                UseShellExecute = false,
            };
            return psi;
        }

        var direct = new ProcessStartInfo(resolved) { UseShellExecute = false };
        foreach (var arg in rest)
            direct.ArgumentList.Add(arg);
        return direct;
    }

    public static string? Resolve(string command)
    {
        if (command.Contains('/') || command.Contains('\\'))
            return File.Exists(command) ? Path.GetFullPath(command) : null;

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var exts = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [""];

        foreach (var dir in dirs)
        {
            var bare = Path.Combine(dir, command);
            if (!OperatingSystem.IsWindows() && File.Exists(bare))
                return bare;
            foreach (var ext in exts)
            {
                var withExt = bare + ext;
                if (File.Exists(withExt))
                    return withExt;
            }
        }
        return null;
    }

    private static bool IsBatch(string path) =>
        path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    private static string CmdQuote(string arg)
    {
        if (arg.Length > 0 && !arg.Any(c => c is ' ' or '\t' or '"'))
            return arg;
        return '"' + arg.Replace("\"", "\"\"") + '"';
    }
}
