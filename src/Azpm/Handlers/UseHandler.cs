namespace Azpm.Handlers;

/// <summary>
/// <c>azpm use &lt;name&gt;</c> — print shell code that points the current shell at a profile.
/// Only takes effect when eval'd — <c>azpm init</c> installs a wrapper that does that for you.
/// </summary>
public sealed class UseHandler(ProfileStore store, TextWriter output, TextWriter error)
{
    public int Run(string name, string? shellName, bool emit)
    {
        var profile = store.Load(name);
        var kind = Shells.Detect(shellName);

        output.Write(ShellIntegration.UseScript(kind, store.Home, profile));
        store.TouchLastUsed(name);

        // Not called through the `azpm init` wrapper → the exports above went nowhere.
        if (!emit && Environment.GetEnvironmentVariable(ShellIntegration.Marker) != "1")
        {
            error.WriteLine($"azpm: 'use' only works through shell integration — nothing changed.");
            error.WriteLine($"  one-time setup:  add  {ShellIntegration.SetupLine(kind)}  to {ShellIntegration.ProfileFile(kind)}");
            error.WriteLine($"  or, no setup:    azpm shell {name}   (opens a subshell)");
        }
        return ExitCode.Ok;
    }
}

/// <summary><c>azpm deactivate</c> — print shell code that clears the profile env.</summary>
public sealed class DeactivateHandler(TextWriter output)
{
    public int Run(string? shellName)
    {
        output.Write(ShellIntegration.DeactivateScript(Shells.Detect(shellName)));
        return ExitCode.Ok;
    }
}

/// <summary><c>azpm init &lt;shell&gt;</c> — print the wrapper function to eval in a shell profile.</summary>
public sealed class InitHandler(TextWriter output, TextWriter error)
{
    public int Run(string shellName, bool auto)
    {
        var kind = Shells.Parse(shellName);
        var exe = Environment.ProcessPath
            ?? throw new AzpmException(ExitCode.UsageError, "cannot determine the azpm executable path");

        output.Write(ShellIntegration.InitHeader(kind, auto));
        output.Write(ShellIntegration.InitScript(kind, exe));
        if (auto)
            output.Write(ShellIntegration.AutoHookScript(kind, exe));

        // Printed straight to a terminal (not piped into eval) → the user probably expected it to
        // "just work". Tell them what to do with it.
        if (!Console.IsOutputRedirected)
        {
            error.WriteLine();
            error.WriteLine($"^ that's a shell snippet, not an action. Add this line to {ShellIntegration.ProfileFile(kind)}:");
            error.WriteLine($"    {ShellIntegration.SetupLine(kind, auto)}");
        }
        return ExitCode.Ok;
    }
}
