using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class LocalTests : IDisposable
{
    private readonly string _origCwd = Directory.GetCurrentDirectory();
    private readonly string _work = Path.Combine(Path.GetTempPath(), "azpm-local-" + Guid.NewGuid().ToString("N"));

    public LocalTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_origCwd);
        try { Directory.Delete(_work, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Find_walks_up_to_the_nearest_dotazpm()
    {
        File.WriteAllText(Path.Combine(_work, ".azpm"), "prod\n");
        var deep = Path.Combine(_work, "a", "b", "c");
        Directory.CreateDirectory(deep);

        var found = LocalFile.Find(deep);

        Assert.NotNull(found);
        Assert.Equal("prod", found.Profile);
    }

    [Fact]
    public void Find_ignores_comments_and_blank_lines()
    {
        File.WriteAllText(Path.Combine(_work, ".azpm"), "# team convention\n\n  dev  \n");
        Assert.Equal("dev", LocalFile.Find(_work)!.Profile);
    }

    [Fact]
    public void Find_returns_null_when_no_file_anywhere()
    {
        Assert.Null(LocalFile.Find(_work));
    }

    [Fact]
    public void Set_then_Resolve_round_trips()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        Directory.SetCurrentDirectory(_work);
        var h = new LocalHandler(t.Store, new StringWriter(), new StringWriter());

        Assert.Equal(ExitCode.Ok, h.Set("dev"));
        Assert.True(File.Exists(Path.Combine(_work, ".azpm")));

        var outw = new StringWriter();
        var code = new LocalHandler(t.Store, outw, new StringWriter()).Resolve();
        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal("dev", outw.ToString().Trim());
    }

    [Fact]
    public void Resolve_exits_nonzero_with_no_file()
    {
        using var t = new TempHome();
        Directory.SetCurrentDirectory(_work);
        Assert.Equal(ExitCode.UsageError,
            new LocalHandler(t.Store, new StringWriter(), new StringWriter()).Resolve());
    }

    [Fact]
    public void Resolve_flags_a_trusted_dotazpm_that_names_a_missing_profile()
    {
        using var t = new TempHome();
        Directory.SetCurrentDirectory(_work);
        File.WriteAllText(Path.Combine(_work, ".azpm"), "ghost\n");
        new LocalHandler(t.Store, new StringWriter(), new StringWriter()).Allow();

        var errw = new StringWriter();
        var code = new LocalHandler(t.Store, new StringWriter(), errw).Resolve();

        Assert.Equal(ExitCode.ProfileNotFound, code);
        Assert.Contains("ghost", errw.ToString());
    }

    [Fact]
    public void Resolve_does_not_follow_an_untrusted_dotazpm()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        Directory.SetCurrentDirectory(_work);
        File.WriteAllText(Path.Combine(_work, ".azpm"), "prod\n");

        var outw = new StringWriter();
        var code = new LocalHandler(t.Store, outw, new StringWriter()).Resolve();

        Assert.Equal(ExitCode.NotTrusted, code);
        Assert.Equal("", outw.ToString().Trim());
    }

    [Fact]
    public void Resolve_follows_an_untrusted_dotazpm_when_trustAll()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        Directory.SetCurrentDirectory(_work);
        File.WriteAllText(Path.Combine(_work, ".azpm"), "prod\n");

        var outw = new StringWriter();
        var code = new LocalHandler(t.Store, outw, new StringWriter()).Resolve(trustAll: true);

        Assert.Equal(ExitCode.Ok, code);
        Assert.Equal("prod", outw.ToString().Trim());
    }

    [Fact]
    public void Allow_then_Resolve_follows_the_file()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        Directory.SetCurrentDirectory(_work);
        File.WriteAllText(Path.Combine(_work, ".azpm"), "prod\n");

        Assert.Equal(ExitCode.Ok, new LocalHandler(t.Store, new StringWriter(), new StringWriter()).Allow());
        Assert.Equal(ExitCode.Ok, new LocalHandler(t.Store, new StringWriter(), new StringWriter()).Resolve());
    }

    [Fact]
    public void Editing_a_trusted_dotazpm_revokes_trust()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        t.Store.Create("dev", null, null);
        Directory.SetCurrentDirectory(_work);
        var file = Path.Combine(_work, ".azpm");
        File.WriteAllText(file, "prod\n");
        new LocalHandler(t.Store, new StringWriter(), new StringWriter()).Allow();

        File.WriteAllText(file, "dev\n"); // someone changed which identity this dir selects

        Assert.Equal(ExitCode.NotTrusted,
            new LocalHandler(t.Store, new StringWriter(), new StringWriter()).Resolve());
    }

    [Fact]
    public void Set_trusts_the_file_it_writes()
    {
        using var t = new TempHome();
        t.Store.Create("prod", null, null);
        Directory.SetCurrentDirectory(_work);

        new LocalHandler(t.Store, new StringWriter(), new StringWriter()).Set("prod");

        Assert.Equal(ExitCode.Ok, new LocalHandler(t.Store, new StringWriter(), new StringWriter()).Resolve());
    }

    [Fact]
    public void Resolve_ignores_a_dotazpm_with_a_traversal_name()
    {
        using var t = new TempHome();
        Directory.SetCurrentDirectory(_work);
        File.WriteAllText(Path.Combine(_work, ".azpm"), "../../../etc\n");

        var errw = new StringWriter();
        var code = new LocalHandler(t.Store, new StringWriter(), errw).Resolve();

        Assert.Equal(ExitCode.UsageError, code);
        Assert.Contains("not a valid profile name", errw.ToString());
    }

    [Fact]
    public void Set_rejects_an_unknown_profile()
    {
        using var t = new TempHome();
        Directory.SetCurrentDirectory(_work);
        Assert.Throws<AzpmException>(
            () => new LocalHandler(t.Store, new StringWriter(), new StringWriter()).Set("nope"));
    }

    [Fact]
    public void Unset_removes_the_file()
    {
        using var t = new TempHome();
        Directory.SetCurrentDirectory(_work);
        File.WriteAllText(Path.Combine(_work, ".azpm"), "dev\n");

        new LocalHandler(t.Store, new StringWriter(), new StringWriter()).Unset();

        Assert.False(File.Exists(Path.Combine(_work, ".azpm")));
    }
}

public sealed class AutoHookTests
{
    [Theory]
    [InlineData(ShellKind.Pwsh)]
    [InlineData(ShellKind.Bash)]
    [InlineData(ShellKind.Zsh)]
    [InlineData(ShellKind.Fish)]
    public void AutoHookScript_calls_local_resolve_and_tracks_AZPM_AUTO(ShellKind kind)
    {
        var script = ShellIntegration.AutoHookScript(kind, "/opt/azpm");
        Assert.Contains("local --resolve", script);
        Assert.Contains("AZPM_AUTO", script);
    }
}
