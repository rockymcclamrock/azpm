using System.Security.Cryptography;
using System.Text.Json;

namespace Azpm;

/// <summary>
/// Records which <c>.azpm</c> files the user has explicitly approved for <c>init --auto</c>
/// following (direnv-style). A checked-in <c>.azpm</c> in a repo you clone can name any existing
/// profile; without approval the auto-hook must not silently switch your Azure identity to it.
/// Keyed by absolute path, valued by a hash of the file's contents, so an edit revokes trust.
/// </summary>
public sealed class LocalTrust(AzpmHome home)
{
    public bool IsTrusted(string dotAzpmPath)
    {
        var key = Key(dotAzpmPath);
        return Read().TryGetValue(key, out var stored)
            && File.Exists(dotAzpmPath)
            && stored == HashOf(dotAzpmPath);
    }

    public void Allow(string dotAzpmPath)
    {
        var map = Read();
        map[Key(dotAzpmPath)] = HashOf(dotAzpmPath);
        Write(map);
    }

    public void Forget(string dotAzpmPath)
    {
        var map = Read();
        if (map.Remove(Key(dotAzpmPath)))
            Write(map);
    }

    private static string Key(string path) => Path.GetFullPath(path);

    private static string HashOf(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private Dictionary<string, string> Read()
    {
        try
        {
            if (File.Exists(home.TrustPath))
            {
                using var fs = File.OpenRead(home.TrustPath);
                return JsonSerializer.Deserialize(fs, AzpmJson.Default.DictionaryStringString)
                    ?? new(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private void Write(Dictionary<string, string> map)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(home.TrustPath)!);
        using var fs = File.Create(home.TrustPath);
        JsonSerializer.Serialize(fs, map, AzpmJson.Default.DictionaryStringString);
    }
}
