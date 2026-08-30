using System.Diagnostics;

namespace Azpm;

public enum BrowserKind { Default, Edge, Chrome, Firefox }

public static class Browsers
{
    public static BrowserKind Parse(string name) => name.ToLowerInvariant() switch
    {
        "default" => BrowserKind.Default,
        "edge" or "msedge" => BrowserKind.Edge,
        "chrome" or "google-chrome" => BrowserKind.Chrome,
        "firefox" => BrowserKind.Firefox,
        _ => throw new AzpmException(ExitCode.UsageError,
            $"unknown browser '{name}' (expected: edge, chrome, firefox, default)"),
    };

    public static string Name(BrowserKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>
    /// Builds the process to open <paramref name="url"/> in the given browser + browser-profile.
    /// Returns null to mean "hand the URL to the OS default handler".
    /// </summary>
    public static ProcessStartInfo? BuildLaunch(BrowserKind kind, string? profile, string url)
    {
        if (kind == BrowserKind.Default)
            return null;

        var exe = FindBrowser(kind)
            ?? throw new AzpmException(ExitCode.UsageError,
                $"{Name(kind)} not found — install it or use '--browser default'");

        // UseShellExecute so the browser is fully detached — otherwise it inherits our stdio
        // handles and any capturing parent shell hangs until the browser exits.
        var psi = new ProcessStartInfo(exe) { UseShellExecute = true };
        switch (kind)
        {
            case BrowserKind.Edge or BrowserKind.Chrome:
                if (!string.IsNullOrEmpty(profile))
                    psi.ArgumentList.Add($"--profile-directory={profile}");
                psi.ArgumentList.Add(url);
                break;

            case BrowserKind.Firefox:
                if (!string.IsNullOrEmpty(profile))
                {
                    psi.ArgumentList.Add("-P");
                    psi.ArgumentList.Add(profile);
                }
                psi.ArgumentList.Add("-new-tab");
                psi.ArgumentList.Add(url);
                break;
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

    private static string? FindBrowser(BrowserKind kind)
    {
        string[] onPath = kind switch
        {
            BrowserKind.Edge => ["msedge", "microsoft-edge", "microsoft-edge-stable"],
            BrowserKind.Chrome => ["chrome", "google-chrome", "google-chrome-stable"],
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
                case BrowserKind.Firefox:
                    yield return "/Applications/Firefox.app/Contents/MacOS/firefox";
                    break;
            }
        }
    }
}
