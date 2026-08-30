using Azpm;
using Xunit;

namespace Azpm.Tests;

public sealed class ProfileStoreTests
{
    [Fact]
    public void ListNames_empty_when_no_profiles_dir()
    {
        using var t = new TempHome();
        Assert.Empty(t.Store.ListNames());
    }

    [Fact]
    public void Create_makes_config_dir_and_meta()
    {
        using var t = new TempHome();
        var p = t.Store.Create("dev", "my dev tenant", "contoso.example.com");

        Assert.True(Directory.Exists(t.Home.ConfigDir("dev")));
        Assert.True(File.Exists(t.Home.MetaPath("dev")));
        Assert.Equal("dev", p.Meta!.Name);
        Assert.Equal("my dev tenant", p.Meta.Description);
        Assert.Equal("contoso.example.com", p.Meta.TenantHint);
        Assert.Equal("logged out", p.Status);
    }

    [Fact]
    public void Create_twice_throws_usage_error()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var ex = Assert.Throws<AzpmException>(() => t.Store.Create("dev", null, null));
        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-leading-dash")]
    [InlineData("has space")]
    [InlineData("current")]
    [InlineData("slash/name")]
    public void Create_rejects_bad_names(string name)
    {
        using var t = new TempHome();
        Assert.Throws<AzpmException>(() => t.Store.Create(name, null, null));
    }

    [Fact]
    public void Load_reads_active_subscription_from_azureProfile()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        t.WriteAzureProfile("prod", "u@p.example.com", "p.example.com", "Prod Sub");

        var p = t.Store.Load("prod");
        Assert.Equal("ready", p.Status);
        Assert.Equal("u@p.example.com", p.ActiveSubscription!.User!.Name);
        Assert.Equal("Prod Sub", p.ActiveSubscription.Name);
    }

    [Fact]
    public void Load_missing_profile_throws_not_found()
    {
        using var t = new TempHome();
        var ex = Assert.Throws<AzpmException>(() => t.Store.Load("nope"));
        Assert.Equal(ExitCode.ProfileNotFound, ex.ExitCode);
    }

    [Fact]
    public void Delete_removes_the_directory()
    {
        using var t = new TempHome();
        t.Store.Create("gone", null, null);
        t.Store.Delete("gone");
        Assert.False(t.Store.Exists("gone"));
    }
}
