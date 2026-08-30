using Azpm;
using Xunit;

namespace Azpm.Tests;

public sealed class CommandResolverTests
{
    [Fact]
    public void Resolve_finds_dotnet_on_path()
    {
        var path = CommandResolver.Resolve("dotnet");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Resolve_returns_null_for_unknown_command()
    {
        Assert.Null(CommandResolver.Resolve("azpm-does-not-exist-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void BuildStartInfo_carries_args_and_disables_shell_execute()
    {
        var psi = CommandResolver.BuildStartInfo(["dotnet", "--info"]);
        Assert.False(psi.UseShellExecute);
        Assert.Contains("--info", psi.ArgumentList);
        Assert.True(Path.IsPathRooted(psi.FileName) || psi.FileName is "cmd.exe");
    }

    [Fact]
    public void BuildStartInfo_throws_when_command_missing()
    {
        var ex = Assert.Throws<AzpmException>(
            () => CommandResolver.BuildStartInfo(["azpm-nope-" + Guid.NewGuid().ToString("N")]));
        Assert.Equal(ExitCode.AzFailed, ex.ExitCode);
    }

    [Fact]
    public void BuildStartInfo_wraps_a_batch_file_with_a_spaced_path_via_cmd()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var dir = Path.Combine(Path.GetTempPath(), "azpm cmd test " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var batch = Path.Combine(dir, "tool.cmd");
            File.WriteAllText(batch, "@echo %AZPM_PROFILE%\r\n");

            var psi = CommandResolver.BuildStartInfo([batch, "--flag", "a value"]);

            Assert.EndsWith("cmd.exe", psi.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("/d /s /c \"", psi.Arguments);
            Assert.Contains($"\"{batch}\"", psi.Arguments);
            Assert.Contains("\"a value\"", psi.Arguments);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
