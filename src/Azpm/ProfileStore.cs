using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Azpm;

/// <summary>Reads and writes the profile directories under <see cref="AzpmHome.ProfilesDir"/>.</summary>
public sealed class ProfileStore(AzpmHome home)
{
    public AzpmHome Home { get; } = home;

    public IEnumerable<string> ListNames()
    {
        if (!Directory.Exists(Home.ProfilesDir))
            return [];
        return Directory.EnumerateDirectories(Home.ProfilesDir)
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
    }

    public bool Exists(string name) => Directory.Exists(Home.ProfileDir(name));

    public Profile Load(string name)
    {
        if (!Exists(name))
            throw new AzpmException(ExitCode.ProfileNotFound, $"profile '{name}' not found");

        return new Profile
        {
            Name = name,
            ConfigDir = Home.ConfigDir(name),
            Meta = ReadJson(Home.MetaPath(name), AzpmJson.Default.ProfileMeta),
            AzureProfile = ReadJson(
                Path.Combine(Home.ConfigDir(name), "azureProfile.json"),
                AzpmJson.Default.AzureProfileFile),
        };
    }

    public IEnumerable<Profile> LoadAll() => ListNames().Select(Load);

    /// <summary>Creates an empty profile (config dir + meta.json). Does not log in.</summary>
    public Profile Create(string name, string? description, string? tenantHint)
    {
        ProfileName.Validate(name);
        if (Exists(name))
            throw new AzpmException(ExitCode.UsageError,
                $"profile '{name}' already exists (use 'azpm login {name}' to re-authenticate)");

        Directory.CreateDirectory(Home.ConfigDir(name));
        WriteJson(Home.MetaPath(name), new ProfileMeta
        {
            Name = name,
            Created = DateTimeOffset.UtcNow,
            Description = description,
            TenantHint = tenantHint,
        }, AzpmJson.Default.ProfileMeta);

        return Load(name);
    }

    public void Delete(string name)
    {
        if (!Exists(name))
            throw new AzpmException(ExitCode.ProfileNotFound, $"profile '{name}' not found");
        Directory.Delete(Home.ProfileDir(name), recursive: true);
    }

    public void TouchLastUsed(string name) =>
        UpdateMeta(name, m => m.LastUsed = DateTimeOffset.UtcNow);

    /// <summary>Reads <c>meta.json</c>, applies <paramref name="mutate"/>, writes it back. No-op if absent.</summary>
    public void UpdateMeta(string name, Action<ProfileMeta> mutate)
    {
        var meta = ReadJson(Home.MetaPath(name), AzpmJson.Default.ProfileMeta);
        if (meta is null) return;
        mutate(meta);
        WriteJson(Home.MetaPath(name), meta, AzpmJson.Default.ProfileMeta);
    }

    private static T? ReadJson<T>(string path, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize(fs, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteJson<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        JsonSerializer.Serialize(fs, value, typeInfo);
    }
}
