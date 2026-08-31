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
            .Where(ProfileName.IsValid) // ignore stray / hand-made directories
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
    }

    public bool Exists(string name) => Directory.Exists(Home.ProfileDir(name));

    public Profile Load(string name)
    {
        ProfileName.Validate(name); // reject traversal / junk before it touches the filesystem
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

        var dir = Home.ProfileDir(name);
        // az spawns a telemetry uploader that briefly holds a handle in the config dir after a
        // login, so a delete right afterwards can lose the race. Retry for ~2s.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100);
            }
        }
        throw new AzpmException(ExitCode.UsageError,
            $"could not fully remove '{dir}' — a file is still in use; try again in a moment");
    }

    public void TouchLastUsed(string name) =>
        UpdateMeta(name, m => m.LastUsed = DateTimeOffset.UtcNow);

    /// <summary>Show/hide a profile from the <c>azpm mcp</c> server.</summary>
    public void SetMcpHidden(string name, bool hidden)
    {
        if (!Exists(name))
            throw new AzpmException(ExitCode.ProfileNotFound, $"profile '{name}' not found");
        UpdateMeta(name, m => m.McpHidden = hidden ? true : null);
    }

    /// <summary>Records a fresh authentication (also counts as a use).</summary>
    public void MarkLoggedIn(string name) =>
        UpdateMeta(name, m =>
        {
            var now = DateTimeOffset.UtcNow;
            m.LastLogin = now;
            m.LastUsed = now;
        });

    public string SpPath(string name) => Path.Combine(Home.ProfileDir(name), "sp.json");

    public ServicePrincipal? ReadServicePrincipal(string name)
    {
        var sp = ReadJson(SpPath(name), AzpmJson.Default.ServicePrincipal);
        if (sp is { SecretProtected: { Length: > 0 } enc } && OperatingSystem.IsWindows())
        {
            try
            {
                sp.Secret = System.Text.Encoding.UTF8.GetString(
                    Dpapi.Unprotect(Convert.FromBase64String(enc)));
            }
            catch (Exception ex) when (ex is FormatException or System.ComponentModel.Win32Exception)
            {
                throw new AzpmException(ExitCode.UsageError,
                    $"could not decrypt the stored secret for '{name}' (was it copied from another machine or user?)");
            }
        }
        return sp;
    }

    /// <summary>
    /// Writes <c>sp.json</c>. On Windows the secret is DPAPI-encrypted at rest (per-user, no key
    /// management). On POSIX it's plaintext, <c>0600</c>. The OS keychain (#9) supersedes both.
    /// The passed <paramref name="sp"/> is not modified.
    /// </summary>
    public void WriteServicePrincipal(string name, ServicePrincipal sp)
    {
        var onDisk = new ServicePrincipal
        {
            ClientId = sp.ClientId,
            TenantId = sp.TenantId,
            Auth = sp.Auth,
            Secret = sp.Secret,
            CertificatePath = sp.CertificatePath,
        };
        if (OperatingSystem.IsWindows() && onDisk.Secret is { Length: > 0 } plain)
        {
            onDisk.SecretProtected = Convert.ToBase64String(
                Dpapi.Protect(System.Text.Encoding.UTF8.GetBytes(plain)));
            onDisk.Secret = null;
        }

        var path = SpPath(name);
        WriteJson(path, onDisk, AzpmJson.Default.ServicePrincipal);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        UpdateMeta(name, m => m.Kind = "service-principal");
    }

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
