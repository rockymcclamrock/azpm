namespace Azpm;

/// <summary>
/// The azpm home directory and the paths within it. Resolution order (SPEC.md §6):
/// <c>--home</c> flag &gt; <c>AZPM_HOME</c> env &gt; <c>%USERPROFILE%\.azpm</c> (Windows) /
/// <c>$XDG_DATA_HOME/azpm</c> else <c>~/.azpm</c>.
/// </summary>
public sealed class AzpmHome(string root)
{
    public string Root { get; } = root;

    public string ProfilesDir => Path.Combine(Root, "profiles");
    public string ProfileDir(string name) => Path.Combine(ProfilesDir, name);
    public string ConfigDir(string name) => Path.Combine(ProfilesDir, name, "config");
    public string MetaPath(string name) => Path.Combine(ProfilesDir, name, "meta.json");

    public static AzpmHome Resolve(string? homeOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(homeOverride))
            return new AzpmHome(Path.GetFullPath(homeOverride));

        var env = Environment.GetEnvironmentVariable("AZPM_HOME");
        if (!string.IsNullOrWhiteSpace(env))
            return new AzpmHome(Path.GetFullPath(env));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
            return new AzpmHome(Path.Combine(userProfile, ".azpm"));

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return new AzpmHome(!string.IsNullOrWhiteSpace(xdg)
            ? Path.Combine(xdg, "azpm")
            : Path.Combine(userProfile, ".azpm"));
    }
}
