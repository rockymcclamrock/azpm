namespace Azpm;

/// <summary>The environment variables azpm injects into a profile's child process / shell.</summary>
public static class ProfileEnv
{
    public const string ConfigDir = "AZURE_CONFIG_DIR";
    public const string Profile = "AZPM_PROFILE";
    public const string Home = "AZPM_HOME";

    public static void Apply(IDictionary<string, string?> env, AzpmHome home, Profile profile)
    {
        env[ConfigDir] = profile.ConfigDir;
        env[Profile] = profile.Name;
        env[Home] = home.Root;
    }
}
