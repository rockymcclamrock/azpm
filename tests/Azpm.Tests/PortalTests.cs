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
    public void Portal_persists_the_browser_mapping()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        var opener = new FakeUrlOpener();

        new PortalHandler(t.Store, opener, TextWriter.Null, TextWriter.Null)
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

        new PortalHandler(t.Store, opener, TextWriter.Null, TextWriter.Null).Run("prod", null, null, null);

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

        new PortalHandler(t.Store, opener, TextWriter.Null, err).Run("prod", null, null, null);

        Assert.Contains("--browser edge", err.ToString());
    }

    [Fact]
    public void Portal_unknown_profile_throws()
    {
        using var t = new TempHome();
        var ex = Assert.Throws<AzpmException>(() =>
            new PortalHandler(t.Store, new FakeUrlOpener(), TextWriter.Null, TextWriter.Null)
                .Run("nope", null, null, null));
        Assert.Equal(ExitCode.ProfileNotFound, ex.ExitCode);
    }

    [Theory]
    [InlineData("edge", BrowserKind.Edge)]
    [InlineData("chrome", BrowserKind.Chrome)]
    [InlineData("firefox", BrowserKind.Firefox)]
    [InlineData("default", BrowserKind.Default)]
    public void Browsers_Parse_maps_names(string name, BrowserKind expected) =>
        Assert.Equal(expected, Browsers.Parse(name));

    [Fact]
    public void Browsers_Parse_rejects_unknown() =>
        Assert.Throws<AzpmException>(() => Browsers.Parse("safari"));
}
