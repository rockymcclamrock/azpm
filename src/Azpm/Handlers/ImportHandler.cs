namespace Azpm.Handlers;

/// <summary>
/// <c>azpm import &lt;name&gt;</c> — turn an existing Azure CLI config directory (by default
/// <c>~/.azure</c>) into an azpm profile, so a login you already have becomes a profile without
/// re-authenticating.
/// </summary>
public sealed class ImportHandler(ProfileStore store, TextWriter output)
{
    public int Run(string name, string? fromDir)
    {
        var src = ResolveSource(fromDir, store.Home);

        if (!Directory.Exists(src))
            throw new AzpmException(ExitCode.UsageError, $"source directory not found: {src}");
        if (!File.Exists(Path.Combine(src, "azureProfile.json")))
            throw new AzpmException(ExitCode.UsageError,
                $"{src} doesn't look like an Azure CLI config dir (no azureProfile.json)");

        var profile = store.Create(name, $"imported from {src}", null);
        CopyDirectory(src, profile.ConfigDir);
        store.MarkLoggedIn(name);

        var sub = store.Load(name).ActiveSubscription;
        output.WriteLine(sub is null
            ? $"Imported '{name}' from {src}."
            : $"Imported '{name}': {sub.User?.Name} @ {sub.TenantDefaultDomain ?? sub.TenantId} ({sub.Name}).");
        return ExitCode.Ok;
    }

    private static string ResolveSource(string? fromDir, AzpmHome home)
    {
        if (!string.IsNullOrWhiteSpace(fromDir))
            return Path.GetFullPath(fromDir);

        var env = Environment.GetEnvironmentVariable(ProfileEnv.ConfigDir);
        if (!string.IsNullOrWhiteSpace(env) &&
            !Path.GetFullPath(env).StartsWith(Path.GetFullPath(home.Root), StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(env);

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".azure");
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            if (IsReparsePoint(dir))
                continue; // don't follow symlinked directories out of the tree
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(src, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            if (IsReparsePoint(file))
                continue;
            var target = Path.Combine(dest, Path.GetRelativePath(src, file));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
            catch (IOException)
            {
                // Skip files az is holding open (telemetry.log, logs/*) — they're just caches.
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
