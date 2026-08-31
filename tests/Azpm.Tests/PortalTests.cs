using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class FakeUrlOpener : IUrlOpener
{
    public BrowserKind Kind { get; private set; }
    public string? Profile { get; private set; }
    public string? Url { get; private set; }
    public bool Launched { get; set; } = true;

    public bool Open(BrowserKind kind, string? browserProfile, string url)
    {
        Kind = kind;
        Profile = browserProfile;
        Url = url;
        return Launched;
    }
}

/// <summary>No browser data by default; add entries to simulate installed Chromium profiles.</summary>
public sealed class FakeBrowserProfiles : IBrowserProfiles
{
    public Dictionary<BrowserKind, IReadOnlyList<BrowserProfile>> ByKind { get; } = [];
    public List<(BrowserKind Kind, string Dir, string Name)> Seeded { get; } = [];

    public IReadOnlyList<BrowserProfile> List(BrowserKind kind) =>
        ByKind.TryGetValue(kind, out var v) ? v : [];

    public void Seed(BrowserKind kind, string dir, string displayName) =>
        Seeded.Add((kind, dir, displayName));
}

public sealed class PortalTests
{
    [Fact]
    public void BuildUrl_pins_the_tenant_from_the_active_subscription()
    {
        var p = new Profile
        {
            Name = "prod",
            ConfigDir = "x",
            AzureProfile = new AzureProfileFile
            {
                Subscriptions = [new AzureSubscription { IsDefault = true, TenantId = "tid-123" }],
            },
        };
        Assert.Equal("https://portal.azure.com/#@tid-123", PortalHandler.BuildUrl(p, null));
    }

    [Fact]
    public void BuildUrl_appends_a_blade_path()
    {
        var p = new Profile
        {
            Name = "prod",
            ConfigDir = "x",
            AzureProfile = new AzureProfileFile
            {
                Subscriptions = [new AzureSubscription { IsDefault = true, TenantId = "t" }],
            },
        };
        Assert.Equal("https://portal.azure.com/#@t/resource/subscriptions",
            PortalHandler.BuildUrl(p, "/resource/subscriptions"));
    }

    [Fact]
    public void BuildUrl_without_a_tenant_is_the_bare_portal()
    {
        var p = new Profile { Name = "x", ConfigDir = "x" };
        Assert.Equal("https://portal.azure.com/", PortalHandler.BuildUrl(p, null));
    }

    [Fact]
    public void BuildUrl_does_not_add_a_login_hint_query_string()
    {
        var p = new Profile
        {
            Name = "prod",
            ConfigDir = "x",
            AzureProfile = new AzureProfileFile
            {
                Subscriptions = [new AzureSubscription
                {
                    IsDefault = true, TenantId = "t",
                    User = new AzureUser { Name = "me@contoso.com" },
                }],
            },
        };
        var url = PortalHandler.BuildUrl(p, null);
        Assert.DoesNotContain("login_hint", url);
        Assert.DoesNotContain("?", url);
        Assert.Equal("https://portal.azure.com/#@t", url);
    }

    [Fact]
    public void BuildUrl_prefers_the_tenant_domain_over_the_guid()
    {
        var p = new Profile
        {
            Name = "prod",
            ConfigDir = "x",
            AzureProfile = new AzureProfileFile
            {
                Subscriptions = [new AzureSubscription
                {
                    IsDefault = true,
                    TenantId = "00000000-1111-2222-3333-444444444444",
                    TenantDefaultDomain = "contoso.example.com",
                }],
            },
        };
        Assert.Equal("https://portal.azure.com/#@contoso.example.com", PortalHandler.BuildUrl(p, null));
    }

    [Fact]
    public void Portal_persists_the_browser_mapping()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        var opener = new FakeUrlOpener();

        new PortalHandler(t.Store, opener, TextWriter.Null, TextWriter.Null, new FakeBrowserProfiles())
            .Run("prod", null, "edge", "Profile 2");

        var meta = t.Store.Load("prod").Meta!;
        Assert.Equal("edge", meta.Browser!.Kind);
        Assert.Equal("Profile 2", meta.Browser.Profile);
        Assert.Equal(BrowserKind.Edge, opener.Kind);
        Assert.Equal("Profile 2", opener.Profile);
    }

    [Fact]
    public void Portal_reuses_the_saved_mapping_on_later_calls()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        t.Store.UpdateMeta("prod", m => m.Browser = new BrowserMapping { Kind = "chrome", Profile = "Work" });
        var opener = new FakeUrlOpener();

        new PortalHandler(t.Store, opener, TextWriter.Null, TextWriter.Null, new FakeBrowserProfiles()).Run("prod", null, null, null);

        Assert.Equal(BrowserKind.Chrome, opener.Kind);
        Assert.Equal("Work", opener.Profile);
    }

    [Fact]
    public void Portal_hints_about_binding_when_no_mapping_and_default_fallback()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        var opener = new FakeUrlOpener { Launched = false };
        var err = new StringWriter();

        new PortalHandler(t.Store, opener, TextWriter.Null, err, new FakeBrowserProfiles()).Run("prod", null, null, null);

        var text = err.ToString();
        Assert.Contains("--browser", text);
        Assert.Contains("--browsers", text);
    }

    [Fact]
    public void Portal_seeds_a_named_profile_when_the_binding_is_new()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        t.Store.UpdateMeta("prod", m => m.Browser = new BrowserMapping { Kind = "brave", Profile = "acme-prod" });
        var opener = new FakeUrlOpener();
        var browsers = new FakeBrowserProfiles();   // nothing installed → it's a new profile

        new PortalHandler(t.Store, opener, TextWriter.Null, TextWriter.Null, browsers)
            .Run("prod", null, null, null);

        Assert.Contains((BrowserKind.Brave, "acme-prod", "acme-prod"), browsers.Seeded);
        Assert.Equal("acme-prod", opener.Profile);
    }

    [Fact]
    public void Portal_notes_that_it_is_asking_the_browser_to_create_a_new_profile()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        t.WriteAzureProfile("prod", "u@p", "p.example.com", "Sub");
        t.Store.UpdateMeta("prod", m => m.Browser = new BrowserMapping { Kind = "brave", Profile = "acme-prod" });

        var err = new StringWriter();
        new PortalHandler(t.Store, new FakeUrlOpener(), TextWriter.Null, err, new FakeBrowserProfiles())
            .Run("prod", null, null, null);

        Assert.Contains("asking brave to create a new profile 'acme-prod'", err.ToString());
        Assert.Contains("u@p", err.ToString());
    }

    [Fact]
    public void ProfileCreationBlocked_is_false_for_non_chromium_and_does_not_throw()
    {
        Assert.False(Browsers.ProfileCreationBlocked(BrowserKind.Firefox));
        Assert.False(Browsers.ProfileCreationBlocked(BrowserKind.Default));
        _ = Browsers.ProfileCreationBlocked(BrowserKind.Edge); // must not throw on any platform
    }

    [Fact]
    public void Portal_does_not_seed_an_existing_profile()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        t.Store.UpdateMeta("prod", m => m.Browser = new BrowserMapping { Kind = "brave", Profile = "g5-prod" });
        var browsers = new FakeBrowserProfiles
        {
            ByKind = { [BrowserKind.Brave] = [new BrowserProfile("Profile 4", "g5-prod", null)] },
        };

        new PortalHandler(t.Store, new FakeUrlOpener(), TextWriter.Null, TextWriter.Null, browsers)
            .Run("prod", null, null, null);

        Assert.Empty(browsers.Seeded);
    }

    [Fact]
    public void Portal_resolves_a_display_name_to_the_profile_directory()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        t.Store.UpdateMeta("prod", m => m.Browser = new BrowserMapping { Kind = "brave", Profile = "g5-prod" });
        var opener = new FakeUrlOpener();
        var browsers = new FakeBrowserProfiles
        {
            ByKind =
            {
                [BrowserKind.Brave] = [new BrowserProfile("Profile 4", "g5-prod", "svc@contoso.com")],
            },
        };

        new PortalHandler(t.Store, opener, TextWriter.Null, TextWriter.Null, browsers)
            .Run("prod", null, null, null);

        Assert.Equal("Profile 4", opener.Profile);   // launched with the directory, not the label
    }

    [Fact]
    public void Portal_unknown_profile_throws()
    {
        using var t = new TempHome();
        var ex = Assert.Throws<AzpmException>(() =>
            new PortalHandler(t.Store, new FakeUrlOpener(), TextWriter.Null, TextWriter.Null, new FakeBrowserProfiles())
                .Run("nope", null, null, null));
        Assert.Equal(ExitCode.ProfileNotFound, ex.ExitCode);
    }

    [Theory]
    [InlineData("edge", BrowserKind.Edge)]
    [InlineData("chrome", BrowserKind.Chrome)]
    [InlineData("brave", BrowserKind.Brave)]
    [InlineData("firefox", BrowserKind.Firefox)]
    [InlineData("default", BrowserKind.Default)]
    public void Browsers_Parse_maps_names(string name, BrowserKind expected) =>
        Assert.Equal(expected, Browsers.Parse(name));

    [Theory]
    [InlineData("Default", true)]
    [InlineData("Profile 3", true)]
    [InlineData("g5-prod", true)]
    [InlineData("../../x", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("has..dots", false)]
    [InlineData(" leading", false)]
    public void Browsers_IsSafeProfileName(string value, bool ok) =>
        Assert.Equal(ok, Browsers.IsSafeProfileName(value));

    [Fact]
    public void Portal_rejects_a_browser_profile_with_path_separators()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        t.WriteAzureProfile("prod", "u@p", "p.example.com", "Sub");

        var ex = Assert.Throws<AzpmException>(() =>
            new PortalHandler(t.Store, new FakeUrlOpener(), TextWriter.Null, TextWriter.Null, new FakeBrowserProfiles())
                .Run("prod", null, "brave", "../../evil"));
        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
    }

    [Fact]
    public void Browsers_Parse_rejects_unknown() =>
        Assert.Throws<AzpmException>(() => Browsers.Parse("safari"));

    [Theory]
    [InlineData(BrowserKind.Edge, true)]
    [InlineData(BrowserKind.Chrome, true)]
    [InlineData(BrowserKind.Brave, true)]
    [InlineData(BrowserKind.Firefox, false)]
    [InlineData(BrowserKind.Default, false)]
    public void Browsers_IsChromium(BrowserKind kind, bool expected) =>
        Assert.Equal(expected, Browsers.IsChromium(kind));

    [Fact]
    public void ResolveProfile_returns_the_input_unchanged_when_nothing_matches()
    {
        // No browser data available in the test env → treat the value as a literal directory.
        var (dir, matched) = Browsers.ResolveProfile(new List<BrowserProfile>(), "whatever");
        Assert.Equal("whatever", dir);
        Assert.Null(matched);
    }
}
