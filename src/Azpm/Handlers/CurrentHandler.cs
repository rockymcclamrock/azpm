namespace Azpm.Handlers;

/// <summary><c>azpm current</c> — print the active profile (from <c>AZPM_PROFILE</c>).</summary>
public sealed class CurrentHandler(TextWriter output, TextWriter error)
{
    public int Run()
    {
        var current = Environment.GetEnvironmentVariable(ProfileEnv.Profile);
        if (string.IsNullOrEmpty(current))
        {
            error.WriteLine("no active azpm profile in this shell");
            return ExitCode.UsageError;
        }
        output.WriteLine(current);
        return ExitCode.Ok;
    }
}
