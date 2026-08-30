namespace Azpm;

/// <summary>
/// The per-directory <c>.azpm</c> file (like <c>.nvmrc</c>): one line naming the profile that
/// should be active under this directory tree.
/// </summary>
public static class LocalFile
{
    public const string Name = ".azpm";

    public sealed record Resolved(string Profile, string FilePath);

    /// <summary>Walks up from <paramref name="startDir"/> looking for the nearest <c>.azpm</c>.</summary>
    public static Resolved? Find(string startDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDir));
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, Name);
            if (File.Exists(candidate))
            {
                var profile = File.ReadLines(candidate)
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
                if (!string.IsNullOrEmpty(profile))
                    return new Resolved(profile, candidate);
            }
            dir = dir.Parent;
        }
        return null;
    }

    public static string Write(string dir, string profile)
    {
        var path = Path.Combine(Path.GetFullPath(dir), Name);
        File.WriteAllText(path, profile + Environment.NewLine);
        return path;
    }
}
