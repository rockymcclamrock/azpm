using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class ImportTests
{
    private static string MakeAzDir(string account = "u@x.example.com", string domain = "x.example.com")
    {
        var dir = Path.Combine(Path.GetTempPath(), "azpm-import-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "logs"));
        File.WriteAllText(Path.Combine(dir, "azureProfile.json"), $$"""
            { "subscriptions": [ { "id": "s1", "name": "Imported Sub", "isDefault": true,
              "tenantId": "t1", "tenantDefaultDomain": "{{domain}}",
              "user": { "name": "{{account}}", "type": "user" } } ] }
            """);
        File.WriteAllText(Path.Combine(dir, "msal_token_cache.bin"), "not-really-a-cache");
        File.WriteAllText(Path.Combine(dir, "logs", "az.log"), "log");
        return dir;
    }

    [Fact]
    public void Import_copies_the_config_and_creates_a_ready_profile()
    {
        using var t = new TempHome();
        var src = MakeAzDir();
        try
        {
            var code = new ImportHandler(t.Store, TextWriter.Null).Run("mine", src);

            Assert.Equal(ExitCode.Ok, code);
            var p = t.Store.Load("mine");
            Assert.Equal("ready", p.Status);
            Assert.Equal("u@x.example.com", p.ActiveSubscription!.User!.Name);
            Assert.True(File.Exists(Path.Combine(p.ConfigDir, "msal_token_cache.bin")));
            Assert.True(File.Exists(Path.Combine(p.ConfigDir, "logs", "az.log")));
        }
        finally { Directory.Delete(src, recursive: true); }
    }

    [Fact]
    public void Import_copies_nested_dirs_whose_names_repeat_the_source_path()
    {
        using var t = new TempHome();
        var root = Path.Combine(Path.GetTempPath(), "azpm-imp-" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "cfg");
        Directory.CreateDirectory(Path.Combine(src, "cfg", "deep")); // "cfg" recurs below src
        File.WriteAllText(Path.Combine(src, "azureProfile.json"),
            "{\"subscriptions\":[{\"id\":\"s\",\"name\":\"S\",\"isDefault\":true,\"tenantId\":\"t\","
            + "\"user\":{\"name\":\"u@x\",\"type\":\"user\"}}]}");
        File.WriteAllText(Path.Combine(src, "cfg", "deep", "note.txt"), "hi");
        try
        {
            new ImportHandler(t.Store, TextWriter.Null).Run("mine", src);
            var p = t.Store.Load("mine");
            Assert.True(File.Exists(Path.Combine(p.ConfigDir, "cfg", "deep", "note.txt")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Import_rejects_a_dir_without_azureProfile()
    {
        using var t = new TempHome();
        var src = Path.Combine(Path.GetTempPath(), "azpm-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(src);
        try
        {
            var ex = Assert.Throws<AzpmException>(() => new ImportHandler(t.Store, TextWriter.Null).Run("x", src));
            Assert.Equal(ExitCode.UsageError, ex.ExitCode);
        }
        finally { Directory.Delete(src, recursive: true); }
    }

    [Fact]
    public void Import_into_an_existing_name_fails()
    {
        using var t = new TempHome();
        t.Store.Create("taken", null, null);
        var src = MakeAzDir();
        try
        {
            Assert.Throws<AzpmException>(() => new ImportHandler(t.Store, TextWriter.Null).Run("taken", src));
        }
        finally { Directory.Delete(src, recursive: true); }
    }
}

public sealed class RenameTests
{
    [Fact]
    public void Rename_moves_the_dir_and_updates_meta()
    {
        using var t = new TempHome();
        t.Store.Create("old", "note", null);
        t.WriteAzureProfile("old", "u@x.example.com", "x.example.com", "Sub");

        new RenameHandler(t.Store, TextWriter.Null).Run("old", "new");

        Assert.False(t.Store.Exists("old"));
        var p = t.Store.Load("new");
        Assert.Equal("new", p.Meta!.Name);
        Assert.Equal("ready", p.Status);
    }

    [Fact]
    public void Rename_unknown_source_throws_not_found()
    {
        using var t = new TempHome();
        var ex = Assert.Throws<AzpmException>(() => new RenameHandler(t.Store, TextWriter.Null).Run("nope", "x"));
        Assert.Equal(ExitCode.ProfileNotFound, ex.ExitCode);
    }

    [Fact]
    public void Rename_onto_an_existing_name_throws()
    {
        using var t = new TempHome();
        t.Store.Create("a", null, null);
        t.Store.Create("b", null, null);
        Assert.Throws<AzpmException>(() => new RenameHandler(t.Store, TextWriter.Null).Run("a", "b"));
    }

    [Fact]
    public void Rename_validates_the_new_name()
    {
        using var t = new TempHome();
        t.Store.Create("a", null, null);
        Assert.Throws<AzpmException>(() => new RenameHandler(t.Store, TextWriter.Null).Run("a", "bad name"));
    }
}
