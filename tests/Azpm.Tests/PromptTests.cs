using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class PromptTests
{
    private static string Run(string? profile, string? format)
    {
        var prev = Environment.GetEnvironmentVariable(ProfileEnv.Profile);
        Environment.SetEnvironmentVariable(ProfileEnv.Profile, profile);
        try
        {
            var w = new StringWriter();
            new PromptHandler(w).Run(format);
            return w.ToString();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProfileEnv.Profile, prev);
        }
    }

    [Fact]
    public void Prints_nothing_when_no_profile() => Assert.Equal("", Run(null, null));

    [Fact]
    public void Prints_the_bare_name_by_default() => Assert.Equal("prod", Run("prod", null));

    [Fact]
    public void Applies_the_format_template() => Assert.Equal(" (az:prod)", Run("prod", " (az:{})"));

    [Fact]
    public void Format_with_no_active_profile_still_prints_nothing() =>
        Assert.Equal("", Run(null, " (az:{})"));
}
