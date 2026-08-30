namespace Azpm.Handlers;

/// <summary>
/// <c>azpm prompt</c> — prompt-friendly sibling of <c>current</c>: env-only (no disk), always
/// exits 0, prints nothing when no profile is active. Meant to be embedded in a shell prompt or
/// a starship / oh-my-posh custom segment.
/// </summary>
public sealed class PromptHandler(TextWriter output)
{
    public int Run(string? format)
    {
        var profile = Environment.GetEnvironmentVariable(ProfileEnv.Profile);
        if (string.IsNullOrEmpty(profile))
            return ExitCode.Ok;

        var text = string.IsNullOrEmpty(format) ? profile : format.Replace("{}", profile);
        output.Write(text);
        return ExitCode.Ok;
    }
}
