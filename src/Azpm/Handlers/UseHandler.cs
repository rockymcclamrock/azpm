namespace Azpm.Handlers;

/// <summary>
/// <c>azpm use &lt;name&gt;</c> — print shell code that points the current shell at a profile.
/// Meant to be eval'd: <c>azpm init</c> installs a wrapper that does it automatically.
/// </summary>
public sealed class UseHandler(ProfileStore store, TextWriter output, TextWriter error)
{
    public int Run(string name, string? shellName, bool emit)
    {
        var profile = store.Load(name);
        var kind = Shells.Detect(shellName);

        output.Write(ShellIntegration.UseScript(kind, profile.Name, profile.ConfigDir, store.Home.Root));
        store.TouchLastUsed(name);

        if (!emit && Environment.GetEnvironmentVariable(ShellIntegration.Marker) != "1")
            error.WriteLine(
                $"# not wired up — either pipe this to your shell, or add to your profile:  " +
                $"azpm init {ShellIntegration.ShellName(kind)}");
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
public sealed class InitHandler(TextWriter output)
{
    public int Run(string shellName, bool auto)
    {
        var kind = Shells.Parse(shellName);
        var exe = Environment.ProcessPath
            ?? throw new AzpmException(ExitCode.UsageError, "cannot determine the azpm executable path");
        output.Write(ShellIntegration.InitScript(kind, exe));
        if (auto)
            output.Write(ShellIntegration.AutoHookScript(kind, exe));
        return ExitCode.Ok;
    }
}
