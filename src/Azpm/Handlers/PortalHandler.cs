using System.ComponentModel;
using System.Diagnostics;

namespace Azpm.Handlers;

/// <summary>Opens a URL in a specific browser + browser-profile (or the OS default).</summary>
public interface IUrlOpener
{
    /// <summary>Returns true if launched in the requested browser, false if it fell back to the OS default.</summary>
    bool Open(BrowserKind kind, string? browserProfile, string url);
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
public sealed class PortalHandler(ProfileStore store, IUrlOpener opener, TextWriter output, TextWriter error)
{
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

        var launched = opener.Open(kind, mapping?.Profile, url);
        store.TouchLastUsed(name);

        if (launched)
            output.WriteLine($"Opened {url} in {Browsers.Name(kind)}" +
                (string.IsNullOrEmpty(mapping?.Profile) ? "." : $" / {mapping!.Profile}."));
        else if (mapping is null)
            error.WriteLine(
                $"# opened in your default browser. To bind '{name}' to a browser profile:  " +
                $"azpm portal {name} --browser edge --browser-profile \"Profile 1\"");
        else
            output.WriteLine($"Opened {url}.");

        return ExitCode.Ok;
    }

    public static string BuildUrl(Profile profile, string? path)
    {
        var tenant = profile.ActiveSubscription?.TenantId
            ?? profile.ActiveSubscription?.TenantDefaultDomain
            ?? profile.Meta?.TenantHint;

        var url = "https://portal.azure.com/";
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
