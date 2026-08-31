using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class PickTests
{
    private static PickHandler Make(TempHome t, string input, TextWriter? outw = null) =>
        new(t.Store, new StringReader(input), outw ?? TextWriter.Null, TextWriter.Null);

    [Fact]
    public void Choosing_a_number_returns_that_profile()
    {
        using var t = new TempHome();
        t.Store.Create("alpha", null, null);
        t.Store.Create("beta", null, null);

        Assert.Equal("beta", Make(t, "2\n").Choose());   // sorted: alpha=1, beta=2
    }

    [Fact]
    public void Blank_input_cancels()
    {
        using var t = new TempHome();
        t.Store.Create("alpha", null, null);
        Assert.Null(Make(t, "\n").Choose());
    }

    [Fact]
    public void Q_cancels()
    {
        using var t = new TempHome();
        t.Store.Create("alpha", null, null);
        Assert.Null(Make(t, "q\n").Choose());
    }

    [Fact]
    public void Out_of_range_returns_null()
    {
        using var t = new TempHome();
        t.Store.Create("alpha", null, null);
        Assert.Null(Make(t, "9\n").Choose());
    }

    [Fact]
    public void No_profiles_prints_a_hint_and_returns_null()
    {
        using var t = new TempHome();
        var outw = new StringWriter();
        Assert.Null(Make(t, "", outw).Choose());
        Assert.Contains("azpm add", outw.ToString());
    }

    [Fact]
    public void The_list_shows_numbers_and_accounts()
    {
        using var t = new TempHome();
        t.Store.Create("alpha", null, null);
        t.WriteAzureProfile("alpha", "a@x.example.com", "x.example.com", "Sub A");
        var outw = new StringWriter();

        Make(t, "\n", outw).Choose();
        var text = outw.ToString();

        Assert.Contains("1", text);
        Assert.Contains("alpha", text);
        Assert.Contains("a@x.example.com", text);
    }
}
