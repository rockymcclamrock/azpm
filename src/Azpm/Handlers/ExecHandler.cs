using System.ComponentModel;
using System.Diagnostics;

namespace Azpm.Handlers;

/// <summary><c>azpm exec &lt;name&gt; -- &lt;cmd&gt; [args...]</c> — run one command in a profile.</summary>
public sealed class ExecHandler(ProfileStore store)
{
    public int Run(string name, IReadOnlyList<string> command)
    {
        if (command.Count == 0)
            throw new AzpmException(ExitCode.UsageError,
                "nothing to run — usage: azpm exec <name> -- <command> [args...]");

        var profile = store.Load(name);
        var psi = CommandResolver.BuildStartInfo(command);
        ProfileEnv.Apply(psi.Environment, store.Home, profile);

        try
        {
            using var process = Process.Start(psi)
                ?? throw new AzpmException(ExitCode.AzFailed, $"failed to start '{command[0]}'");
            process.WaitForExit();
            store.TouchLastUsed(name);
            return process.ExitCode;
        }
        catch (Win32Exception ex)
        {
            throw new AzpmException(ExitCode.AzFailed, $"cannot run '{command[0]}': {ex.Message}");
        }
    }
}
