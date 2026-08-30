using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class LoginHandlerTests
{
    [Fact]
    public void Login_reauths_an_existing_profile()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var az = new FakeAzRunner();

        var code = new LoginHandler(t.Store, az, TextWriter.Null).Run("dev", new InteractiveLogin(null, false), null, reset: false);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal("login", az.Calls.Single().Args[0]);
        Assert.Equal("ready", t.Store.Load("dev").Status);
    }

    [Fact]
    public void Login_unknown_profile_throws_not_found()
    {
        using var t = new TempHome();
        var ex = Assert.Throws<AzpmException>(
            () => new LoginHandler(t.Store, new FakeAzRunner(), TextWriter.Null).Run("nope", new InteractiveLogin(null, false), null, false));
        Assert.Equal(ExitCode.ProfileNotFound, ex.ExitCode);
    }

    [Fact]
    public void Login_reset_clears_prior_state_before_login()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        t.WriteAzureProfile("dev", "old@x.example.com", "x.example.com", "Old");
        File.WriteAllText(Path.Combine(t.Home.ConfigDir("dev"), "leftover.txt"), "x");

        var az = new FakeAzRunner { LoginAccount = "new@y.example.com" };
        new LoginHandler(t.Store, az, TextWriter.Null).Run("dev", new InteractiveLogin(null, false), null, reset: true);

        Assert.False(File.Exists(Path.Combine(t.Home.ConfigDir("dev"), "leftover.txt")));
        Assert.Equal("new@y.example.com", t.Store.Load("dev").ActiveSubscription!.User!.Name);
    }

    [Fact]
    public void Login_failure_surfaces_as_az_failed()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var ex = Assert.Throws<AzpmException>(
            () => new LoginHandler(t.Store, new FakeAzRunner { ExitCode = 1 }, TextWriter.Null)
                .Run("dev", new InteractiveLogin(null, false), null, false));
        Assert.Equal(ExitCode.AzFailed, ex.ExitCode);
    }
}

public sealed class LogoutHandlerTests
{
    [Fact]
    public void Logout_runs_az_logout_and_flips_status()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        t.WriteAzureProfile("dev", "u@x.example.com", "x.example.com", "Sub");
        var az = new FakeAzRunner();

        new LogoutHandler(t.Store, az, TextWriter.Null).Run("dev");

        Assert.Equal("logout", az.Calls.Single().Args[0]);
        Assert.Equal("logged out", t.Store.Load("dev").Status);
    }

    [Fact]
    public void Logout_is_a_noop_when_already_logged_out()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var az = new FakeAzRunner();

        new LogoutHandler(t.Store, az, TextWriter.Null).Run("dev");

        Assert.Empty(az.Calls);
    }
}

public sealed class RmHandlerTests
{
    [Fact]
    public void Rm_with_yes_deletes_without_prompting()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        new RmHandler(t.Store, new StringReader(""), TextWriter.Null, TextWriter.Null).Run("dev", yes: true);
        Assert.False(t.Store.Exists("dev"));
    }

    [Fact]
    public void Rm_prompt_y_deletes()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        new RmHandler(t.Store, new StringReader("y\n"), TextWriter.Null, TextWriter.Null).Run("dev", yes: false);
        Assert.False(t.Store.Exists("dev"));
    }

    [Fact]
    public void Rm_prompt_no_keeps_the_profile()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var code = new RmHandler(t.Store, new StringReader("\n"), TextWriter.Null, TextWriter.Null).Run("dev", false);
        Assert.Equal(ExitCode.Ok, code);
        Assert.True(t.Store.Exists("dev"));
    }

    [Fact]
    public void Rm_unknown_profile_throws_not_found()
    {
        using var t = new TempHome();
        var ex = Assert.Throws<AzpmException>(
            () => new RmHandler(t.Store, new StringReader(""), TextWriter.Null, TextWriter.Null).Run("nope", true));
        Assert.Equal(ExitCode.ProfileNotFound, ex.ExitCode);
    }
}
