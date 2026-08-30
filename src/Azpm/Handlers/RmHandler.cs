namespace Azpm.Handlers;

/// <summary><c>azpm rm &lt;name&gt;</c> — delete a profile directory (prompts unless <c>--yes</c>).</summary>
public sealed class RmHandler(ProfileStore store, TextReader input, TextWriter output, TextWriter error)
{
    public int Run(string name, bool yes)
    {
        if (!store.Exists(name))
            throw new AzpmException(ExitCode.ProfileNotFound, $"profile '{name}' not found");

        if (!yes)
        {
            error.Write($"Delete profile '{name}' and its login state? [y/N] ");
            var answer = input.ReadLine()?.Trim();
            if (!IsYes(answer))
            {
                output.WriteLine("cancelled");
                return ExitCode.Ok;
            }
        }

        store.Delete(name);
        output.WriteLine($"Removed '{name}'.");
        return ExitCode.Ok;
    }

    private static bool IsYes(string? s) =>
        string.Equals(s, "y", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase);
}
