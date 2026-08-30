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

        var code = az.Run(profile.ConfigDir, ["logout"]);
        store.TouchLastUsed(name);

        var after = store.Load(name);
        if (after.Status != "ready")
            output.WriteLine($"Logged out of '{name}'.");
        else if (code == 0)
            output.WriteLine($"Signed out the active account in '{name}' (other accounts remain).");
        else
            output.WriteLine($"'az logout' exited {code}; '{name}' may still be signed in.");
        return ExitCode.Ok;
    }
}
