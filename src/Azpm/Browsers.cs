using System.Diagnostics;
using System.Text.Json;

namespace Azpm;

public enum BrowserKind { Default, Edge, Chrome, Brave, Firefox }

/// <summary>A profile found in a Chromium browser's <c>User Data\Local State</c>.</summary>
public sealed record BrowserProfile(string Directory, string DisplayName, string? Account);

public static class Browsers
{
    public static BrowserKind Parse(string name) => name.ToLowerInvariant() switch
    {
        "default" => BrowserKind.Default,
        "edge" or "msedge" => BrowserKind.Edge,
        "chrome" or "google-chrome" => BrowserKind.Chrome,
        "brave" => BrowserKind.Brave,
        "firefox" => BrowserKind.Firefox,
        _ => throw new AzpmException(ExitCode.UsageError,
            $"unknown browser '{name}' (expected: edge, chrome, brave, firefox, default)"),
    };

    public static string Name(BrowserKind kind) => kind.ToString().ToLowerInvariant();

    public static bool IsChromium(BrowserKind kind) =>
        kind is BrowserKind.Edge or BrowserKind.Chrome or BrowserKind.Brave;

    /// <summary>
    /// Builds the process to open <paramref name="url"/> in the given browser + browser-profile.
    /// Returns null to mean "hand the URL to the OS default handler".
    /// </summary>
    public static ProcessStartInfo? BuildLaunch(BrowserKind kind, string? profileDir, string url)
    {
        if (kind == BrowserKind.Default)
            return null;

        var exe = FindBrowser(kind)
            ?? throw new AzpmException(ExitCode.UsageError,
                $"{Name(kind)} not found — install it or use '--browser default'");

        // UseShellExecute so the browser is fully detached — otherwise it inherits our stdio
        // handles and any capturing parent shell hangs until the browser exits.
        var psi = new ProcessStartInfo(exe) { UseShellExecute = true };

        if (IsChromium(kind))
        {
            if (!string.IsNullOrEmpty(profileDir))
                psi.ArgumentList.Add($"--profile-directory={profileDir}");
            psi.ArgumentList.Add(url);
        }
        else // Firefox
        {
            if (!string.IsNullOrEmpty(profileDir))
            {
                psi.ArgumentList.Add("-P");
                psi.ArgumentList.Add(profileDir);
            }
            psi.ArgumentList.Add("-new-tab");
            psi.ArgumentList.Add(url);
        }
        return psi;
    }

    public static void OpenDefault(string url)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }
        var psi = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { UseShellExecute = false };
        psi.ArgumentList.Add(url);
        Process.Start(psi);
    }

    /// <summary>Reads a Chromium browser's profile list from <c>User Data\Local State</c>. Empty if unavailable.</summary>
    public static IReadOnlyList<BrowserProfile> ListProfiles(BrowserKind kind)
    {
        if (!IsChromium(kind))
            return [];

        var localState = ChromiumUserDataDir(kind) is { } dir ? Path.Combine(dir, "Local State") : null;
        if (localState is null || !File.Exists(localState))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(localState));
            if (!doc.RootElement.TryGetProperty("profile", out var p) ||
                !p.TryGetProperty("info_cache", out var cache))
                return [];

            var list = new List<BrowserProfile>();
            foreach (var entry in cache.EnumerateObject())
            {
                var name = entry.Value.TryGetProperty("name", out var n) ? n.GetString() : null;
                var user = entry.Value.TryGetProperty("user_name", out var u) ? u.GetString() : null;
                list.Add(new BrowserProfile(entry.Name, name ?? entry.Name,
                    string.IsNullOrEmpty(user) ? null : user));
            }
            return list.OrderBy(x => x.Directory, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Resolves what the user typed for <c>--browser-profile</c> to a Chromium profile *directory*.
    /// Accepts either the directory (<c>Profile 3</c>) or the display name (<c>g5-dev</c>).
    /// </summary>
    public static (string Dir, BrowserProfile? Matched) ResolveProfile(
        IReadOnlyList<BrowserProfile> profiles, string wanted)
    {
        foreach (var prof in profiles)
        {
            if (string.Equals(prof.Directory, wanted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prof.DisplayName, wanted, StringComparison.OrdinalIgnoreCase))
                return (prof.Directory, prof);
        }
        return (wanted, null); // unknown — Chromium will create a fresh profile with this dir name
    }

    /// <summary>
    /// A <c>--browser-profile</c> value that is safe to use as an on-disk directory name: no path
    /// separators, no <c>..</c>, no control characters. Chromium's own profile dirs
    /// (<c>Default</c>, <c>Profile 3</c>) and any sensible display name pass.
    /// </summary>
    public static bool IsSafeProfileName(string s) =>
        s.Length is > 0 and <= 128
        && s.IndexOfAny(['/', '\\']) < 0
        && !s.Contains("..")
        && !s.Any(char.IsControl)
        && s.Trim() == s;

    /// <summary>
    /// Pre-seeds a not-yet-existing Chromium profile directory with a <c>Preferences</c> file that
    /// sets its display name, so it shows up in the browser as e.g. "g5-prod" rather than
    /// "Person 3". No-op if the directory already exists or the browser is unknown.
    /// </summary>
    public static void SeedChromiumProfile(BrowserKind kind, string dir, string displayName)
    {
        if (!IsChromium(kind) || !IsSafeProfileName(dir) || ChromiumUserDataDir(kind) is not { } userData)
            return;

        var profileDir = Path.Combine(userData, dir);
        // Defence in depth: refuse anything that escaped the User Data dir.
        if (Path.GetRelativePath(userData, profileDir).StartsWith("..", StringComparison.Ordinal))
            return;
        if (Directory.Exists(profileDir))
            return;

        try
        {
            Directory.CreateDirectory(profileDir);
            var prefs = Path.Combine(profileDir, "Preferences");
            if (!File.Exists(prefs))
                File.WriteAllText(prefs,
                    "{\"profile\":{\"name\":\"" + JsonEncodedText.Encode(displayName) + "\"}}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best effort — the browser will still make the profile, just unnamed
        }
    }

    private static string? ChromiumUserDataDir(BrowserKind kind)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return kind switch
            {
                BrowserKind.Edge => Path.Combine(local, @"Microsoft\Edge\User Data"),
                BrowserKind.Chrome => Path.Combine(local, @"Google\Chrome\User Data"),
                BrowserKind.Brave => Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"),
                _ => null,
            };
        }
        if (OperatingSystem.IsMacOS())
        {
            var appSup = Path.Combine(home, "Library", "Application Support");
            return kind switch
            {
                BrowserKind.Edge => Path.Combine(appSup, "Microsoft Edge"),
                BrowserKind.Chrome => Path.Combine(appSup, "Google", "Chrome"),
                BrowserKind.Brave => Path.Combine(appSup, "BraveSoftware", "Brave-Browser"),
                _ => null,
            };
        }
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
        return kind switch
        {
            BrowserKind.Edge => Path.Combine(config, "microsoft-edge"),
            BrowserKind.Chrome => Path.Combine(config, "google-chrome"),
            BrowserKind.Brave => Path.Combine(config, "BraveSoftware", "Brave-Browser"),
            _ => null,
        };
    }

    private static string? FindBrowser(BrowserKind kind)
    {
        string[] onPath = kind switch
        {
            BrowserKind.Edge => ["msedge", "microsoft-edge", "microsoft-edge-stable"],
            BrowserKind.Chrome => ["chrome", "google-chrome", "google-chrome-stable"],
            BrowserKind.Brave => ["brave", "brave-browser", "brave-browser-stable"],
            BrowserKind.Firefox => ["firefox"],
            _ => [],
        };
        foreach (var name in onPath)
            if (CommandResolver.Resolve(name) is { } hit)
                return hit;

        foreach (var candidate in WellKnownPaths(kind))
            if (File.Exists(candidate))
                return candidate;

        return null;
    }

    private static IEnumerable<string> WellKnownPaths(BrowserKind kind)
    {
        if (OperatingSystem.IsWindows())
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            switch (kind)
            {
                case BrowserKind.Edge:
                    yield return Path.Combine(pfx86, @"Microsoft\Edge\Application\msedge.exe");
                    yield return Path.Combine(pf, @"Microsoft\Edge\Application\msedge.exe");
                    break;
                case BrowserKind.Chrome:
                    yield return Path.Combine(pf, @"Google\Chrome\Application\chrome.exe");
                    yield return Path.Combine(pfx86, @"Google\Chrome\Application\chrome.exe");
                    yield return Path.Combine(local, @"Google\Chrome\Application\chrome.exe");
                    break;
                case BrowserKind.Brave:
                    yield return Path.Combine(pf, @"BraveSoftware\Brave-Browser\Application\brave.exe");
                    yield return Path.Combine(pfx86, @"BraveSoftware\Brave-Browser\Application\brave.exe");
                    yield return Path.Combine(local, @"BraveSoftware\Brave-Browser\Application\brave.exe");
                    break;
                case BrowserKind.Firefox:
                    yield return Path.Combine(pf, @"Mozilla Firefox\firefox.exe");
                    yield return Path.Combine(pfx86, @"Mozilla Firefox\firefox.exe");
                    break;
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            switch (kind)
            {
                case BrowserKind.Edge:
                    yield return "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge";
                    break;
                case BrowserKind.Chrome:
                    yield return "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
                    break;
                case BrowserKind.Brave:
                    yield return "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser";
                    break;
                case BrowserKind.Firefox:
                    yield return "/Applications/Firefox.app/Contents/MacOS/firefox";
                    break;
            }
        }
    }
}
