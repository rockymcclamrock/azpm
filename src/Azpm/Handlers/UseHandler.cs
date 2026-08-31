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
            error.WriteLine();
            error.WriteLine($"azpm: nothing changed — 'azpm use' can't modify the shell that ran it.");
            error.WriteLine();
            error.WriteLine($"  To run commands as '{name}' right now:");
            error.WriteLine($"      azpm shell {name}     (starts a shell that is '{name}'; type 'exit' to leave)");

            if (kind != ShellKind.Cmd)
            {
                error.WriteLine();
                error.WriteLine($"  To make 'azpm use' work in your normal shell: add this line to");
                error.WriteLine($"  {ShellIntegration.ProfileFile(kind)}, then open a new shell —");
                error.WriteLine($"      {ShellIntegration.SetupLine(kind)}");
                error.WriteLine($"  (assumed {ShellIntegration.ShellName(kind)}; if that's wrong: azpm use {name} --shell <bash|zsh|fish|powershell>)");
            }
            else
            {
                error.WriteLine();
                error.WriteLine($"  ('azpm use' isn't available in cmd.exe — use 'azpm shell', or switch to PowerShell.)");
            }
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
    public int Run(string shellName, bool auto, bool fullAuto = false)
    {
        var kind = Shells.Parse(shellName);
        var exe = Environment.ProcessPath
            ?? throw new AzpmException(ExitCode.UsageError, "cannot determine the azpm executable path");

        var wantAuto = auto || fullAuto;

        output.Write(ShellIntegration.InitHeader(kind, wantAuto, fullAuto));
        output.Write(ShellIntegration.InitScript(kind, exe));
        if (wantAuto)
            output.Write(ShellIntegration.AutoHookScript(kind, exe, trustAll: fullAuto));

        // Printed straight to a terminal (not piped into eval) → the user probably expected it to
        // "just work". Tell them what to do with it.
        if (!Console.IsOutputRedirected)
        {
            var flag = fullAuto ? " --fullauto" : auto ? " --auto" : "";
            error.WriteLine();
            error.WriteLine($"The text above is a {ShellIntegration.ShellName(kind)} snippet — it does nothing on its own.");
            error.WriteLine($"Add this one line to {ShellIntegration.ProfileFile(kind)} and open a new shell:");
            error.WriteLine($"    {ShellIntegration.SetupLine(kind, auto, fullAuto)}");
            error.WriteLine($"(wrong shell? run:  azpm init <bash|zsh|fish|powershell>{flag})");
        }
        return ExitCode.Ok;
    }
}
