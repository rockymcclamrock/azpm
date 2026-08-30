namespace Azpm.Handlers;

/// <summary><c>azpm logout &lt;name&gt;</c> — <c>az logout</c> in the profile; keep the profile itself.</summary>
public sealed class LogoutHandler(ProfileStore store, IAzRunner az, TextWriter output)
{
    public int Run(string name)
    {
        var profile = store.Load(name);
        if (profile.Status != "ready")
        {
            output.WriteLine($"profile '{name}' is already logged out");
            return ExitCode.Ok;
        }

        // az logout exits non-zero when there's nothing to log out — not an error for us.
        az.Run(profile.ConfigDir, ["logout"]);
        store.TouchLastUsed(name);
        output.WriteLine($"Logged out of '{name}'.");
        return ExitCode.Ok;
    }
}
