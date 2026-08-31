using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class ShellIntegrationTests
{
    private static (AzpmHome, Profile) Sample(string name = "dev") => (
        new AzpmHome(@"C:\x"),
        new Profile
        {
            Name = name,
            ConfigDir = $@"C:\x\profiles\{name}\config",
            AzureProfile = new AzureProfileFile
            {
                Subscriptions = [new AzureSubscription { IsDefault = true, Id = "sub-1", TenantId = "ten-1" }],
            },
        });

    [Theory]
    [InlineData(ShellKind.Pwsh, "$env:AZURE_CONFIG_DIR = ")]
    [InlineData(ShellKind.PowerShell, "$env:AZPM_PROFILE = ")]
    [InlineData(ShellKind.Bash, "export AZURE_CONFIG_DIR=")]
    [InlineData(ShellKind.Zsh, "export AZPM_PROFILE=")]
    [InlineData(ShellKind.Fish, "set -gx AZURE_CONFIG_DIR ")]
    [InlineData(ShellKind.Cmd, "set \"AZURE_CONFIG_DIR=")]
    public void UseScript_uses_the_right_syntax(ShellKind kind, string expectedFragment)
    {
        var (home, profile) = Sample();
        var script = ShellIntegration.UseScript(kind, home, profile);
        Assert.Contains(expectedFragment, script);
        Assert.Contains("dev", script);
    }

    [Fact]
    public void UseScript_exports_ARM_vars_when_logged_in()
    {
        var (home, profile) = Sample();
        var script = ShellIntegration.UseScript(ShellKind.Bash, home, profile);
        Assert.Contains("ARM_SUBSCRIPTION_ID=", script);
        Assert.Contains("ARM_TENANT_ID=", script);
        Assert.Contains("sub-1", script);
    }

    [Fact]
    public void UseScript_omits_ARM_vars_when_logged_out()
    {
        var script = ShellIntegration.UseScript(ShellKind.Bash,
            new AzpmHome("/x"), new Profile { Name = "dev", ConfigDir = "/x/dev" });
        Assert.DoesNotContain("ARM_SUBSCRIPTION_ID", script);
    }

    [Fact]
    public void UseScript_quotes_awkward_paths()
    {
        var script = ShellIntegration.UseScript(ShellKind.Bash,
            new AzpmHome("/home/a b"),
            new Profile { Name = "dev", ConfigDir = "/home/a b/it's" });
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
            var err = errw.ToString();
            Assert.Contains("eval \"$(azpm init bash)\"", err);   // the exact setup line
            Assert.Contains("~/.bashrc", err);
            Assert.Contains("azpm shell dev", err);               // the no-setup alternative
            Assert.DoesNotContain("shell integration", err);      // no jargon
        }
        finally
        {
            Environment.SetEnvironmentVariable(ShellIntegration.Marker, prev);
        }
    }

    [Fact]
    public void Use_hint_for_cmd_points_only_at_azpm_shell()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        var prev = Environment.GetEnvironmentVariable(ShellIntegration.Marker);
        Environment.SetEnvironmentVariable(ShellIntegration.Marker, null);
        try
        {
            var errw = new StringWriter();
            new UseHandler(t.Store, new StringWriter(), errw).Run("dev", "cmd", emit: false);
            var err = errw.ToString();
            Assert.Contains("azpm shell dev", err);
            Assert.Contains("cmd.exe", err);
            Assert.DoesNotContain("azpm init cmd", err);          // never suggest the broken thing
            Assert.DoesNotContain("eval \"$(", err);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ShellIntegration.Marker, prev);
        }
    }

    [Theory]
    [InlineData(ShellKind.PowerShell, "azpm init powershell | Out-String | Invoke-Expression", "$PROFILE")]
    [InlineData(ShellKind.Bash, "eval \"$(azpm init bash)\"", "~/.bashrc")]
    [InlineData(ShellKind.Fish, "azpm init fish | source", "~/.config/fish/config.fish")]
    public void SetupLine_and_ProfileFile_match_the_shell(ShellKind kind, string line, string file)
    {
        Assert.Equal(line, ShellIntegration.SetupLine(kind));
        Assert.Equal(file, ShellIntegration.ProfileFile(kind));
    }

    [Fact]
    public void InitHeader_shows_the_setup_line() =>
        Assert.Contains("eval \"$(azpm init zsh --auto)\"", ShellIntegration.InitHeader(ShellKind.Zsh, auto: true));

    [Theory]
    [InlineData(ShellKind.PowerShell)]
    [InlineData(ShellKind.Bash)]
    [InlineData(ShellKind.Fish)]
    public void AutoHook_uses_the_trust_gated_resolve_by_default(ShellKind kind)
    {
        var hook = ShellIntegration.AutoHookScript(kind, "/x/azpm");
        Assert.Contains("local --resolve", hook);
        Assert.DoesNotContain("--trust-all", hook);
        Assert.Contains("not trusted", hook); // the one-time nudge on exit 5
    }

    [Theory]
    [InlineData(ShellKind.PowerShell)]
    [InlineData(ShellKind.Bash)]
    [InlineData(ShellKind.Fish)]
    public void FullAuto_hook_skips_the_trust_check(ShellKind kind)
    {
        var hook = ShellIntegration.AutoHookScript(kind, "/x/azpm", trustAll: true);
        Assert.Contains("local --resolve --trust-all", hook);
    }

    [Fact]
    public void SetupLine_reflects_fullauto() =>
        Assert.Equal("eval \"$(azpm init bash --fullauto)\"",
            ShellIntegration.SetupLine(ShellKind.Bash, auto: true, fullAuto: true));

    [Fact]
    public void Deactivate_emits_an_unset_for_the_shell()
    {
        var outw = new StringWriter();
        new DeactivateHandler(outw).Run("bash");
        var script = outw.ToString();
        Assert.Contains("unset AZURE_CONFIG_DIR", script);
        Assert.Contains("unset AZPM_PROFILE", script);
        Assert.Contains("unset ARM_SUBSCRIPTION_ID", script);
    }
}
