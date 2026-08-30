using Azpm;
using Xunit;

namespace Azpm.Tests;

public sealed class ShellsTests
{
    [Theory]
    [InlineData("pwsh", ShellKind.Pwsh)]
    [InlineData("PowerShell", ShellKind.PowerShell)]
    [InlineData("cmd", ShellKind.Cmd)]
    [InlineData("bash", ShellKind.Bash)]
    [InlineData("zsh", ShellKind.Zsh)]
    [InlineData("fish", ShellKind.Fish)]
    public void Parse_maps_known_names(string name, ShellKind expected) =>
        Assert.Equal(expected, Shells.Parse(name));

    [Fact]
    public void Parse_rejects_unknown_shell()
    {
        var ex = Assert.Throws<AzpmException>(() => Shells.Parse("tcsh"));
        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
    }

    [Fact]
    public void Detect_prefers_the_explicit_name() =>
        Assert.Equal(ShellKind.Fish, Shells.Detect("fish"));

    [Fact]
    public void Build_powershell_prefixes_the_prompt()
    {
        var launch = Shells.Build(ShellKind.PowerShell, "prod");
        Assert.Contains(launch.StartInfo.ArgumentList, a => a.Contains("[azpm:prod]"));
        Assert.Empty(launch.TempPaths);
    }

    [Fact]
    public void Build_bash_writes_an_rcfile_that_sources_bashrc_and_tags_the_prompt()
    {
        var launch = Shells.Build(ShellKind.Bash, "prod");
        try
        {
            var rcfile = launch.StartInfo.ArgumentList[1];
            var contents = File.ReadAllText(rcfile);
            Assert.Contains("~/.bashrc", contents);
            Assert.Contains("[azpm:prod]", contents);
        }
        finally
        {
            foreach (var p in launch.TempPaths) File.Delete(p);
        }
    }

    [Fact]
    public void Build_zsh_sets_zdotdir_to_a_temp_dir()
    {
        var launch = Shells.Build(ShellKind.Zsh, "prod");
        try
        {
            Assert.Equal(launch.TempPaths[0], launch.StartInfo.Environment["ZDOTDIR"]);
            Assert.Contains("[azpm:prod]", File.ReadAllText(Path.Combine(launch.TempPaths[0], ".zshrc")));
        }
        finally
        {
            foreach (var p in launch.TempPaths) Directory.Delete(p, recursive: true);
        }
    }
}
