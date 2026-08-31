using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class AddHandlerTests
{
    [Fact]
    public void Add_creates_profile_and_calls_az_login_with_config_dir()
    {
        using var t = new TempHome();
        var az = new FakeAzRunner();
        var code = new AddHandler(t.Store, az, TextWriter.Null).Run("dev", new InteractiveLogin(null, false), null, null);

        Assert.Equal(ExitCode.Ok, code);
        var call = Assert.Single(az.Calls);
        Assert.Equal(t.Home.ConfigDir("dev"), call.ConfigDir);
        Assert.Equal("login", call.Args[0]);
        Assert.Equal("ready", t.Store.Load("dev").Status);
    }

    [Fact]
    public void Add_passes_tenant_and_device_code_through()
    {
        using var t = new TempHome();
        var az = new FakeAzRunner();
        new AddHandler(t.Store, az, TextWriter.Null).Run("dev", new InteractiveLogin("contoso.example.com", true), null, null);

        var args = az.Calls.Single().Args;
        Assert.Contains("--tenant", args);
        Assert.Contains("contoso.example.com", args);
        Assert.Contains("--use-device-code", args);
    }

    [Fact]
    public void Add_rolls_back_when_az_login_fails()
    {
        using var t = new TempHome();
        var az = new FakeAzRunner { ExitCode = 1 };

        var ex = Assert.Throws<AzpmException>(
            () => new AddHandler(t.Store, az, TextWriter.Null).Run("dev", new InteractiveLogin(null, false), null, null));

        Assert.Equal(ExitCode.AzFailed, ex.ExitCode);
        Assert.False(t.Store.Exists("dev"));
    }
}

public sealed class LsHandlerTests
{
    [Fact]
    public void Ls_reports_no_profiles()
    {
        using var t = new TempHome();
        var sw = new StringWriter();
        new LsHandler(t.Store, sw).Run(json: false);
        Assert.Contains("No profiles yet", sw.ToString());
    }

    [Fact]
    public void Ls_table_lists_account_and_status()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        t.WriteAzureProfile("dev", "dev@contoso.example.com", "contoso.example.com", "Dev Sub");
        t.Store.Create("prod", null, null); // no login -> logged out

        var sw = new StringWriter();
        new LsHandler(t.Store, sw).Run(json: false);
        var output = sw.ToString();

        Assert.Contains("dev@contoso.example.com", output);
        Assert.Contains("Dev Sub", output);
        Assert.Contains("logged out", output);
    }

    [Fact]
    public void Ls_check_probes_ready_profiles_and_reports_token_state()
    {
        using var t = new TempHome();
        t.Store.Create("good", null, null);
        t.WriteAzureProfile("good", "u@x", "x.example.com", "Sub");
        t.Store.Create("stale", null, null);
        t.WriteAzureProfile("stale", "u@y", "y.example.com", "Sub");
        t.Store.Create("out", null, null); // logged out -> not probed

        var az = new FakeAzRunner();
        // one runner instance, so make it fail: exercises the "needs login" branch
        az.CaptureResult = new AzResult(1, "", "", false);

        var sw = new StringWriter();
        new LsHandler(t.Store, sw, () => az).Run(json: false, check: true);
        var outp = sw.ToString();

        Assert.Contains("needs login", outp);
        Assert.Contains("logged out", outp);          // "out" profile, never probed
        Assert.Equal(2, az.Calls.Count(c => c.Args is ["account", "get-access-token", ..]));
    }

    [Fact]
    public void Ls_check_marks_valid_when_az_succeeds()
    {
        using var t = new TempHome();
        t.Store.Create("good", null, null);
        t.WriteAzureProfile("good", "u@x", "x.example.com", "Sub");

        var sw = new StringWriter();
        new LsHandler(t.Store, sw, () => new FakeAzRunner()).Run(json: false, check: true);

        Assert.Contains("valid", sw.ToString());
    }

    [Fact]
    public void Ls_json_is_valid_and_ordered()
    {
        using var t = new TempHome();
        t.Store.Create("b", null, null);
        t.Store.Create("a", null, null);

        var sw = new StringWriter();
        new LsHandler(t.Store, sw).Run(json: true);
        using var doc = System.Text.Json.JsonDocument.Parse(sw.ToString());

        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!).ToArray();
        Assert.Equal(["a", "b"], names);
    }

    [Fact]
    public void Ls_shows_relative_login_age_from_meta()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        t.WriteAzureProfile("dev", "u@x", "x.example.com", "Sub");
        t.Store.UpdateMeta("dev", m => m.LastLogin = DateTimeOffset.UtcNow.AddHours(-5));
        t.Store.Create("fresh", null, null); // never logged in -> "-"

        var sw = new StringWriter();
        new LsHandler(t.Store, sw).Run(json: false);
        var output = sw.ToString();

        Assert.Contains("LOGIN", output);
        Assert.Contains("5h ago", output);
    }

    [Fact]
    public void Ls_check_still_shows_the_login_age_from_meta()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        t.WriteAzureProfile("dev", "u@x", "x.example.com", "Sub");
        t.Store.UpdateMeta("dev", m => m.LastLogin = DateTimeOffset.UtcNow.AddDays(-2));

        var sw = new StringWriter();
        new LsHandler(t.Store, sw, () => new FakeAzRunner()).Run(json: false, check: true);
        var output = sw.ToString();

        Assert.Contains("valid", output);
        Assert.Contains("2d ago", output);
    }
}
