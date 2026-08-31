using System.ComponentModel;
using System.Diagnostics;

namespace Azpm.Handlers;

/// <summary>Opens a URL in a specific browser + browser-profile (or the OS default).</summary>
public interface IUrlOpener
{
    /// <summary>Returns true if launched in the requested browser, false if it fell back to the OS default.</summary>
    bool Open(BrowserKind kind, string? browserProfileDir, string url);
}

/// <summary>Reads / seeds a Chromium browser's profiles.</summary>
public interface IBrowserProfiles
{
    IReadOnlyList<BrowserProfile> List(BrowserKind kind);
    void Seed(BrowserKind kind, string dir, string displayName);
}

public sealed class SystemBrowserProfiles : IBrowserProfiles
{
    public IReadOnlyList<BrowserProfile> List(BrowserKind kind) => Browsers.ListProfiles(kind);
    public void Seed(BrowserKind kind, string dir, string displayName) =>
        Browsers.SeedChromiumProfile(kind, dir, displayName);
}

public sealed class UrlOpener : IUrlOpener
{
    public bool Open(BrowserKind kind, string? browserProfile, string url)
    {
        var psi = Browsers.BuildLaunch(kind, browserProfile, url);
        if (psi is null)
        {
            Browsers.OpenDefault(url);
            return false;
        }
        try
        {
            Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new AzpmException(ExitCode.UsageError, $"cannot launch {Browsers.Name(kind)}: {ex.Message}");
        }
        return true;
    }
}

/// <summary><c>azpm portal &lt;name&gt;</c> — open the Azure Portal in the profile's browser context.</summary>
public sealed class PortalHandler(
    ProfileStore store, IUrlOpener opener, TextWriter output, TextWriter error, IBrowserProfiles? browsers = null)
{
    private readonly IBrowserProfiles _browsers = browsers ?? new SystemBrowserProfiles();

    /// <summary><c>azpm portal --browsers</c> — list the Chromium browser profiles azpm can see.</summary>
    public int ListBrowsers()
    {
        var any = false;
        foreach (var kind in new[] { BrowserKind.Edge, BrowserKind.Chrome, BrowserKind.Brave })
        {
            var profiles = _browsers.List(kind);
            if (profiles.Count == 0)
                continue;
            any = true;
            output.WriteLine($"{Browsers.Name(kind)}:");
            var table = new TextTable("  --browser-profile", "SHOWN AS", "ACCOUNT");
            foreach (var p in profiles)
                table.AddRow($"  {p.Directory}", p.DisplayName, p.Account ?? "-");
            table.RenderTo(output);
            output.WriteLine();
        }
        if (!any)
            output.WriteLine("No Edge/Chrome/Brave profiles found.");
        else
            output.WriteLine("Pass either the left column or \"SHOWN AS\" to --browser-profile.");
        return ExitCode.Ok;
    }

    public int Run(string name, string? path, string? browser, string? browserProfile)
    {
        var profile = store.Load(name);

        if (browser is not null || browserProfile is not null)
        {
            var kindName = browser ?? profile.Meta?.Browser?.Kind ?? "default";
            store.UpdateMeta(name, m => m.Browser = new BrowserMapping
            {
                Kind = Browsers.Name(Browsers.Parse(kindName)),
                Profile = browserProfile ?? profile.Meta?.Browser?.Profile,
            });
            profile = store.Load(name);
        }

        var url = BuildUrl(profile, path);
        var mapping = profile.Meta?.Browser;
        var kind = mapping is null ? BrowserKind.Default : Browsers.Parse(mapping.Kind);
        var account = profile.ActiveSubscription?.User?.Name;

        // Resolve "g5-dev" (display name) or "Profile 3" (directory) to the directory Chromium wants.
        string? launchDir = mapping?.Profile;
        BrowserProfile? matched = null;
        if (!string.IsNullOrEmpty(mapping?.Profile) && Browsers.IsChromium(kind))
        {
            var known = _browsers.List(kind);
            (launchDir, matched) = Browsers.ResolveProfile(known, mapping.Profile);
            if (matched is null)
            {
                // New profile — name it after the binding so it reads as "g5-prod" in the browser.
                _browsers.Seed(kind, launchDir, mapping.Profile);
                error.WriteLine(
                    $"note: creating a new {Browsers.Name(kind)} profile '{mapping.Profile}' — " +
                    $"sign in with {account ?? "your account"} when its window opens.");
            }
        }

        var launched = opener.Open(kind, launchDir, url);
        store.TouchLastUsed(name);

        if (launched)
        {
            var where = matched is not null
                ? $"{Browsers.Name(kind)} / {matched.DisplayName}"
                : string.IsNullOrEmpty(launchDir) ? Browsers.Name(kind) : $"{Browsers.Name(kind)} / {launchDir}";
            output.WriteLine($"Opened the portal in {where}{(account is null ? "." : $" ({account}).")}");
            if (matched?.Account is not null && account is not null &&
                !string.Equals(matched.Account, account, StringComparison.OrdinalIgnoreCase))
                error.WriteLine($"warning: that browser profile is signed in as {matched.Account}, not {account}.");
        }
        else if (mapping is null)
        {
            error.WriteLine(
                $"opened in your default browser (no isolation). Bind one to skip the account picker:\n" +
                $"  azpm portal --browsers                      # see your options\n" +
                $"  azpm portal {name} --browser brave --browser-profile \"<name>\"");
        }
        else
        {
            output.WriteLine("Opened the portal.");
        }

        return ExitCode.Ok;
    }

    public static string BuildUrl(Profile profile, string? path)
    {
        var tenant = profile.ActiveSubscription?.TenantId
            ?? profile.ActiveSubscription?.TenantDefaultDomain
            ?? profile.Meta?.TenantHint;
        var account = profile.ActiveSubscription?.User?.Name;

        var url = "https://portal.azure.com/";

        // login_hint nudges the sign-in page toward the right account when the browser profile
        // has several signed in. Best-effort — the surest fix is one account per browser profile.
        if (!string.IsNullOrEmpty(account))
            url += $"?login_hint={Uri.EscapeDataString(account)}";

        if (!string.IsNullOrEmpty(tenant))
            url += $"#@{tenant}";

        if (!string.IsNullOrWhiteSpace(path))
        {
            var trimmed = path.TrimStart('/');
            url += string.IsNullOrEmpty(tenant) ? $"#{trimmed}" : $"/{trimmed}";
        }
        return url;
    }
}
