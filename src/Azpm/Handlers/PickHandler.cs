namespace Azpm.Handlers;

/// <summary>
/// Bare <c>azpm</c> with a terminal attached: a numbered profile list you pick from, which then
/// drops you into that profile's shell. Non-interactive callers get a short usage instead.
/// </summary>
public sealed class PickHandler(ProfileStore store, TextReader input, TextWriter output, TextWriter error)
{
    /// <summary>Returns the chosen profile name, or null if the user bailed / there's nothing to pick.</summary>
    public string? Choose()
    {
        var profiles = store.LoadAll().ToList();
        if (profiles.Count == 0)
        {
            output.WriteLine("No profiles yet. Create one:  azpm add <name>");
            return null;
        }

        var current = Environment.GetEnvironmentVariable(ProfileEnv.Profile);
        var table = new TextTable("", "#", "NAME", "ACCOUNT", "SUBSCRIPTION", "STATUS");
        for (var i = 0; i < profiles.Count; i++)
        {
            var p = profiles[i];
            var sub = p.ActiveSubscription;
            table.AddRow(p.Name == current ? "*" : "", $"{i + 1}", p.Name,
                sub?.User?.Name ?? "-", sub?.Name ?? "-", p.Status);
        }
        table.RenderTo(output);

        error.Write($"Open a shell for which profile? [1-{profiles.Count}, or Enter to cancel] ");
        var line = input.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(line) || line is "q" or "Q")
            return null;
        if (int.TryParse(line, out var n) && n >= 1 && n <= profiles.Count)
            return profiles[n - 1].Name;

        error.WriteLine($"azpm: '{line}' isn't one of 1-{profiles.Count}");
        return null;
    }
}
