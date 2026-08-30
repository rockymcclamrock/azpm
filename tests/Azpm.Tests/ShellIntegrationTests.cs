using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class ShellIntegrationTests
{
    [Theory]
    [InlineData(ShellKind.Pwsh, "$env:AZURE_CONFIG_DIR = ")]
    [InlineData(ShellKind.PowerShell, "$env:AZPM_PROFILE = ")]
    [InlineData(ShellKind.Bash, "export AZURE_CONFIG_DIR=")]
    [InlineData(ShellKind.Zsh, "export AZPM_PROFILE=")]
    [InlineData(ShellKind.Fish, "set -gx AZURE_CONFIG_DIR ")]
    [InlineData(ShellKind.Cmd, "set \"AZURE_CONFIG_DIR=")]
    public void UseScript_uses_the_right_syntax(ShellKind kind, string expectedFragment)
    {
        var script = ShellIntegration.UseScript(kind, "dev", @"C:\x\dev\config", @"C:\x");
        Assert.Contains(expectedFragment, script);
        Assert.Contains("dev", script);
    }

    [Fact]
    public void UseScript_quotes_awkward_paths()
    {
        var script = ShellIntegration.UseScript(ShellKind.Bash, "dev", "/home/a b/it's", "/home/a b");
        Assert.Contains(@"'/home/a b/it'\''s'", script);
    }

    [Theory]
    [InlineData(ShellKind.Pwsh)]
    [InlineData(ShellKind.Bash)]
    [InlineData(ShellKind.Fish)]
    public void InitScript_defines_an_azpm_function_that_calls_the_exe(ShellKind kind)
    {
        var script = ShellIntegration.InitScript(kind, "/opt/azpm");
        Assert.Contains("azpm", script);
        Assert.Contains("--emit", script);
        Assert.Contains("/opt/azpm", script);
    }

    [Fact]
    public void InitScript_rejects_cmd()
    {
        var ex = Assert.Throws<AzpmException>(() => ShellIntegration.InitScript(ShellKind.Cmd, "x"));
        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
    }

    [Fact]
    public void Use_emits_script_and_no_hint_when_emit()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var outw = new StringWriter();
        var errw = new StringWriter();

        new UseHandler(t.Store, outw, errw).Run("dev", "bash", emit: true);

        Assert.Contains("export AZPM_PROFILE='dev'", outw.ToString());
        Assert.Empty(errw.ToString());
    }

    [Fact]
    public void Use_prints_setup_hint_when_not_wired_up()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var prev = Environment.GetEnvironmentVariable(ShellIntegration.Marker);
        Environment.SetEnvironmentVariable(ShellIntegration.Marker, null);
        try
        {
            var errw = new StringWriter();
            new UseHandler(t.Store, new StringWriter(), errw).Run("dev", "bash", emit: false);
            Assert.Contains("azpm init bash", errw.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ShellIntegration.Marker, prev);
        }
    }

    [Fact]
    public void Deactivate_emits_an_unset_for_the_shell()
    {
        var outw = new StringWriter();
        new DeactivateHandler(outw).Run("bash");
        Assert.Contains("unset AZURE_CONFIG_DIR AZPM_PROFILE", outw.ToString());
    }
}
