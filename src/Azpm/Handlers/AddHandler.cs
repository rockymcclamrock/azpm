namespace Azpm.Handlers;

/// <summary><c>azpm add &lt;name&gt;</c> — create an isolated profile and run <c>az login</c> into it.</summary>
public sealed class AddHandler(ProfileStore store, IAzRunner az, TextWriter output)
{
    public int Run(string name, string? tenant, bool deviceCode, string? description)
    {
        var profile = store.Create(name, description, tenant);

        var loginArgs = new List<string> { "login" };
        if (!string.IsNullOrWhiteSpace(tenant))
        {
            loginArgs.Add("--tenant");
            loginArgs.Add(tenant);
        }
        if (deviceCode)
            loginArgs.Add("--use-device-code");

        output.WriteLine($"Logging in to profile '{name}'...");
        var code = az.Run(profile.ConfigDir, loginArgs);
        if (code != 0)
        {
            // Roll back the half-created profile so a failed login leaves nothing behind.
            store.Delete(name);
            throw new AzpmException(ExitCode.AzFailed,
                $"'az login' failed (exit {code}); profile '{name}' was not created");
        }

        store.TouchLastUsed(name);
        var loaded = store.Load(name);
        var sub = loaded.ActiveSubscription;
        output.WriteLine(sub is null
            ? $"Profile '{name}' created."
            : $"Profile '{name}' ready: {sub.User?.Name} @ {sub.TenantDefaultDomain ?? sub.TenantId} ({sub.Name}).");
        return ExitCode.Ok;
    }
}
