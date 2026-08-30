namespace Azpm;

/// <summary>The environment variables azpm injects into a profile's child process / shell.</summary>
public static class ProfileEnv
{
    public const string ConfigDir = "AZURE_CONFIG_DIR";
    public const string Profile = "AZPM_PROFILE";
    public const string Home = "AZPM_HOME";

    /// <summary>Set from the active subscription when the profile is logged in — for Terraform / SDKs.</summary>
    public static readonly string[] Derived = ["ARM_SUBSCRIPTION_ID", "ARM_TENANT_ID", "AZURE_SUBSCRIPTION_ID"];

    /// <summary>Everything <c>use</c> sets, so <c>deactivate</c> knows what to clear (keeps AZPM_HOME).</summary>
    public static IReadOnlyList<string> ClearOnDeactivate { get; } = [ConfigDir, Profile, .. Derived];

    public static IReadOnlyDictionary<string, string> Collect(AzpmHome home, Profile profile)
    {
        var d = new Dictionary<string, string>
        {
            [ConfigDir] = profile.ConfigDir,
            [Profile] = profile.Name,
            [Home] = home.Root,
        };

        var sub = profile.ActiveSubscription;
        if (sub?.Id is { Length: > 0 } id)
        {
            d["ARM_SUBSCRIPTION_ID"] = id;
            d["AZURE_SUBSCRIPTION_ID"] = id;
        }
        if (sub?.TenantId is { Length: > 0 } tid)
            d["ARM_TENANT_ID"] = tid;

        return d;
    }

    public static void Apply(IDictionary<string, string?> env, AzpmHome home, Profile profile)
    {
        foreach (var (k, v) in Collect(home, profile))
            env[k] = v;
    }
}
