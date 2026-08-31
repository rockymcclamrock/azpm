namespace Azpm.Handlers;

/// <summary>What <c>az login</c> should do for a profile that isn't a stored service principal.</summary>
public sealed record InteractiveLogin(string? Tenant, bool DeviceCode);

/// <summary><c>azpm login &lt;name&gt;</c> — (re-)authenticate an existing profile.</summary>
public sealed class LoginHandler(ProfileStore store, IAzRunner az, TextWriter output)
{
    /// <summary>
    /// Re-auth a profile. <paramref name="sp"/> (from <c>add --service-principal</c> or
    /// <c>login --client-secret</c>) is persisted then used; otherwise a stored <c>sp.json</c> is
    /// used if present, else it's an interactive login.
    /// </summary>
    public int Run(string name, InteractiveLogin interactive, ServicePrincipal? sp, bool reset)
    {
        var profile = store.Load(name);
        var before = profile.ActiveSubscription?.User?.Name;

        if (reset && Directory.Exists(profile.ConfigDir))
        {
            Directory.Delete(profile.ConfigDir, recursive: true);
            Directory.CreateDirectory(profile.ConfigDir);
        }

        if (sp is not null)
            store.WriteServicePrincipal(name, sp);
        sp ??= store.ReadServicePrincipal(name);

        output.WriteLine($"Logging in to profile '{name}'{(sp is not null ? " (service principal)" : "")}...");

        int code;
        if (sp is not null)
        {
            // Keep the client secret off the process command line — write it to an owner-only
            // file inside the (already-protected) profile dir and hand `az` a `@path` reference.
            var secretFile = sp.Auth == "certificate" ? null : WriteSecretFile(profile.ConfigDir, sp.Secret!);
            try
            {
                code = az.Run(profile.ConfigDir, ServicePrincipalArgs(sp, secretFile));
            }
            finally
            {
                if (secretFile is not null)
                    TryDelete(secretFile);
            }
        }
        else
        {
            code = az.Run(profile.ConfigDir, InteractiveArgs(interactive));
        }

        if (code != 0)
            throw new AzpmException(ExitCode.AzFailed, $"'az login' failed (exit {code})");

        store.MarkLoggedIn(name);
        var after = store.Load(name).ActiveSubscription;

        if (before is not null && after?.User?.Name is { } now && now != before && sp is null)
            output.WriteLine(
                $"note: profile '{name}' now has logins for both '{before}' and '{now}'. " +
                $"Run 'azpm login {name} --reset' to start from a clean profile.");

        output.WriteLine(after is null
            ? $"Logged in to '{name}'."
            : $"Profile '{name}': {after.User?.Name} @ {after.TenantDefaultDomain ?? after.TenantId} ({after.Name}).");
        return ExitCode.Ok;
    }

    private static List<string> InteractiveArgs(InteractiveLogin i)
    {
        var args = new List<string> { "login" };
        if (!string.IsNullOrWhiteSpace(i.Tenant))
        {
            args.Add("--tenant");
            args.Add(i.Tenant);
        }
        if (i.DeviceCode)
            args.Add("--use-device-code");
        return args;
    }

    private static List<string> ServicePrincipalArgs(ServicePrincipal sp, string? secretFile) =>
    [
        "login", "--service-principal",
        "-u", sp.ClientId,
        .. sp.Auth == "certificate"
            ? new[] { "--certificate", sp.CertificatePath! }
            : ["-p", "@" + secretFile!],
        "--tenant", sp.TenantId,
    ];

    private static string WriteSecretFile(string configDir, string secret)
    {
        Directory.CreateDirectory(configDir);
        var path = Path.Combine(configDir, ".azpm-sp-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, secret);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* best effort — it's inside the profile dir, not /tmp */ }
    }
}
