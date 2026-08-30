using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class ExecHandlerTests
{
    // Deliberately space-free scripts so .NET's argv->command-line quoting can't mangle them.
    private static string[] PrintEnvTo(string file) => OperatingSystem.IsWindows()
        ? ["powershell", "-NoProfile", "-Command",
           $"[IO.File]::WriteAllText('{file}',$env:AZPM_PROFILE+'|'+$env:AZURE_CONFIG_DIR)"]
        : ["sh", "-c", $"printf '%s|%s' \"$AZPM_PROFILE\" \"$AZURE_CONFIG_DIR\">'{file}'"];

    private static string[] ExitWith(int code) => OperatingSystem.IsWindows()
        ? ["cmd", "/c", $"exit {code}"]
        : ["sh", "-c", $"exit {code}"];

    [Fact]
    public void Exec_sets_AZURE_CONFIG_DIR_and_AZPM_PROFILE()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var outFile = Path.Combine(t.Home.Root, "out.txt");

        var code = new ExecHandler(t.Store).Run("dev", PrintEnvTo(outFile));

        Assert.Equal(ExitCode.Ok, code);
        var text = File.ReadAllText(outFile);
        Assert.Contains("dev", text);
        Assert.Contains(t.Home.ConfigDir("dev"), text);
    }

    [Fact]
    public void Exec_returns_the_child_exit_code()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        Assert.Equal(7, new ExecHandler(t.Store).Run("dev", ExitWith(7)));
    }

    [Fact]
    public void Exec_unknown_profile_throws_not_found()
    {
        using var t = new TempHome();
        var ex = Assert.Throws<AzpmException>(() => new ExecHandler(t.Store).Run("nope", ExitWith(0)));
        Assert.Equal(ExitCode.ProfileNotFound, ex.ExitCode);
    }

    [Fact]
    public void Exec_empty_command_is_a_usage_error()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var ex = Assert.Throws<AzpmException>(() => new ExecHandler(t.Store).Run("dev", []));
        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
    }

    [Fact]
    public void Exec_command_not_found_is_reported_cleanly()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var ex = Assert.Throws<AzpmException>(
            () => new ExecHandler(t.Store).Run("dev", ["azpm-nope-" + Guid.NewGuid().ToString("N")]));
        Assert.Equal(ExitCode.AzFailed, ex.ExitCode);
    }
}

public sealed class PathAndCurrentTests
{
    [Fact]
    public void Path_prints_the_config_dir()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var sw = new StringWriter();
        new PathHandler(t.Store, sw).Run("dev");
        Assert.Equal(t.Home.ConfigDir("dev"), sw.ToString().Trim());
    }

    [Fact]
    public void Current_errors_when_no_profile_active()
    {
        var prev = Environment.GetEnvironmentVariable(ProfileEnv.Profile);
        Environment.SetEnvironmentVariable(ProfileEnv.Profile, null);
        try
        {
            var code = new CurrentHandler(new StringWriter(), new StringWriter()).Run();
            Assert.Equal(ExitCode.UsageError, code);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProfileEnv.Profile, prev);
        }
    }

    [Fact]
    public void Current_prints_the_active_profile()
    {
        var prev = Environment.GetEnvironmentVariable(ProfileEnv.Profile);
        Environment.SetEnvironmentVariable(ProfileEnv.Profile, "prod");
        try
        {
            var sw = new StringWriter();
            var code = new CurrentHandler(sw, new StringWriter()).Run();
            Assert.Equal(ExitCode.Ok, code);
            Assert.Equal("prod", sw.ToString().Trim());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProfileEnv.Profile, prev);
        }
    }
}
