namespace Azpm.Handlers;

/// <summary><c>azpm login &lt;name&gt;</c> — (re-)run <c>az login</c> in an existing profile.</summary>
public sealed class LoginHandler(ProfileStore store, IAzRunner az, TextWriter output)
{
    public int Run(string name, string? tenant, bool deviceCode, bool reset)
    {
        var profile = store.Load(name);
        var before = profile.ActiveSubscription?.User?.Name;

        if (reset && Directory.Exists(profile.ConfigDir))
        {
            Directory.Delete(profile.ConfigDir, recursive: true);
            Directory.CreateDirectory(profile.ConfigDir);
        }

        var args = new List<string> { "login" };
        if (!string.IsNullOrWhiteSpace(tenant))
        {
            args.Add("--tenant");
            args.Add(tenant);
        }
        if (deviceCode)
            args.Add("--use-device-code");

        output.WriteLine($"Logging in to profile '{name}'...");
        var code = az.Run(profile.ConfigDir, args);
        if (code != 0)
            throw new AzpmException(ExitCode.AzFailed, $"'az login' failed (exit {code})");

        store.TouchLastUsed(name);
        var after = store.Load(name).ActiveSubscription;

        if (before is not null && after?.User?.Name is { } now && now != before)
            output.WriteLine(
                $"note: profile '{name}' now has logins for both '{before}' and '{now}'. " +
                $"Run 'azpm login {name} --reset' to start from a clean profile.");

        output.WriteLine(after is null
            ? $"Logged in to '{name}'."
            : $"Profile '{name}': {after.User?.Name} @ {after.TenantDefaultDomain ?? after.TenantId} ({after.Name}).");
        return ExitCode.Ok;
    }
}
