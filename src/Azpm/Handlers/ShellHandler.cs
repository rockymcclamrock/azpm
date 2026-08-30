using System.ComponentModel;
using System.Diagnostics;

namespace Azpm.Handlers;

/// <summary><c>azpm shell &lt;name&gt;</c> — open an interactive subshell with the profile active.</summary>
public sealed class ShellHandler(ProfileStore store, TextWriter output, TextWriter error)
{
    public int Run(string name, string? shellName)
    {
        var profile = store.Load(name);

        var already = Environment.GetEnvironmentVariable(ProfileEnv.Profile);
        if (already == name)
        {
            error.WriteLine($"already in a shell for profile '{name}'");
            return ExitCode.UsageError;
        }
        if (!string.IsNullOrEmpty(already))
            error.WriteLine($"note: nesting an azpm shell (current profile: '{already}')");

        var kind = Shells.Detect(shellName);
        var launch = Shells.Build(kind, name);
        Shells.ResolveExecutable(launch.StartInfo);
        ProfileEnv.Apply(launch.StartInfo.Environment, store.Home, profile);

        output.WriteLine($"Entering '{name}' ({kind}). Type 'exit' to leave.");
        try
        {
            using var process = Process.Start(launch.StartInfo)
                ?? throw new AzpmException(ExitCode.AzFailed, $"failed to start {kind}");
            store.TouchLastUsed(name);
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            throw new AzpmException(ExitCode.AzFailed, $"cannot start {kind}: {ex.Message}");
        }
        finally
        {
            foreach (var path in launch.TempPaths)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else File.Delete(path);
                }
                catch (IOException) { /* best effort */ }
            }
        }
    }
}
