using Azpm;
using Azpm.Handlers;
using Xunit;

namespace Azpm.Tests;

public sealed class ServicePrincipalInputTests
{
    private static ServicePrincipal? Resolve(bool sp = false, string? id = null, string? tenant = null,
        string? secret = null, bool stdin = false, string? cert = null, string stdinText = "")
        => ServicePrincipalInput.Resolve(sp, id, tenant, secret, stdin, cert, new StringReader(stdinText));

    [Fact]
    public void No_sp_flags_returns_null() => Assert.Null(Resolve());

    [Fact]
    public void Secret_flow_builds_a_credential()
    {
        var sp = Resolve(sp: true, id: "app-1", tenant: "ten-1", secret: "shh")!;
        Assert.Equal("app-1", sp.ClientId);
        Assert.Equal("ten-1", sp.TenantId);
        Assert.Equal("secret", sp.Auth);
        Assert.Equal("shh", sp.Secret);
    }

    [Fact]
    public void Stdin_secret_is_read_and_trimmed()
    {
        var sp = Resolve(id: "app-1", tenant: "ten-1", stdin: true, stdinText: "  topsecret \n")!;
        Assert.Equal("topsecret", sp.Secret);
    }

    [Fact]
    public void Missing_client_id_is_a_usage_error()
    {
        var ex = Assert.Throws<AzpmException>(() => Resolve(sp: true, tenant: "t", secret: "s"));
        Assert.Equal(ExitCode.UsageError, ex.ExitCode);
    }

    [Fact]
    public void Missing_tenant_is_a_usage_error() =>
        Assert.Throws<AzpmException>(() => Resolve(sp: true, id: "app", secret: "s"));

    [Fact]
    public void Both_secret_and_cert_is_a_usage_error()
    {
        var cert = Path.GetTempFileName();
        try
        {
            Assert.Throws<AzpmException>(() => Resolve(id: "app", tenant: "t", secret: "s", cert: cert));
        }
        finally { File.Delete(cert); }
    }

    [Fact]
    public void Neither_secret_nor_cert_is_a_usage_error() =>
        Assert.Throws<AzpmException>(() => Resolve(sp: true, id: "app", tenant: "t"));

    [Fact]
    public void Missing_cert_file_is_a_usage_error() =>
        Assert.Throws<AzpmException>(() => Resolve(id: "app", tenant: "t", cert: @"C:\nope\missing.pem"));
}

public sealed class ServicePrincipalLoginTests
{
    private static ServicePrincipal Secret(string id = "app-1", string tenant = "ten-1", string secret = "shh")
        => new() { ClientId = id, TenantId = tenant, Auth = "secret", Secret = secret };

    [Fact]
    public void Add_sp_persists_sp_json_and_marks_the_profile()
    {
        using var t = new TempHome();
        var az = new FakeAzRunner();

        new AddHandler(t.Store, az, TextWriter.Null)
            .Run("ci", new InteractiveLogin("ten-1", false), Secret(), null);

        var stored = t.Store.ReadServicePrincipal("ci")!;
        Assert.Equal("app-1", stored.ClientId);
        Assert.Equal("shh", stored.Secret);
        Assert.Equal("service-principal", t.Store.Load("ci").Meta!.Kind);

        var args = az.Calls.Single().Args;
        Assert.Contains("--service-principal", args);
        Assert.Contains("app-1", args);
        Assert.Contains("ten-1", args);
    }

    [Fact]
    public void Login_without_flags_reuses_stored_sp_json()
    {
        using var t = new TempHome();
        t.Store.Create("ci", null, null);
        t.Store.WriteServicePrincipal("ci", Secret());
        var az = new FakeAzRunner();

        new LoginHandler(t.Store, az, TextWriter.Null)
            .Run("ci", new InteractiveLogin(null, false), null, reset: false);

        Assert.Contains("--service-principal", az.Calls.Single().Args);
    }

    [Fact]
    public void Login_keeps_the_secret_off_the_az_command_line()
    {
        using var t = new TempHome();
        t.Store.Create("ci", null, null);
        var az = new FakeAzRunner();

        new LoginHandler(t.Store, az, TextWriter.Null)
            .Run("ci", new InteractiveLogin(null, false), Secret(secret: "topsecret"), reset: false);

        var args = az.Calls.Single().Args;
        Assert.DoesNotContain("topsecret", args);
        var pIndex = Array.IndexOf(args, "-p");
        Assert.True(pIndex >= 0);
        Assert.StartsWith("@", args[pIndex + 1]);           // az reads the secret from @file
    }

    [Fact]
    public void Login_deletes_the_temp_secret_file_afterward()
    {
        using var t = new TempHome();
        t.Store.Create("ci", null, null);

        new LoginHandler(t.Store, new FakeAzRunner(), TextWriter.Null)
            .Run("ci", new InteractiveLogin(null, false), Secret(), reset: false);

        var cfg = t.Home.ConfigDir("ci");
        Assert.Empty(Directory.Exists(cfg)
            ? Directory.GetFiles(cfg, ".azpm-sp-*")
            : []);
    }

    [Fact]
    public void Login_with_certificate_uses_the_certificate_flag()
    {
        using var t = new TempHome();
        t.Store.Create("ci", null, null);
        var cert = Path.Combine(t.Home.Root, "sp.pem");
        File.WriteAllText(cert, "-----BEGIN PRIVATE KEY-----");
        t.Store.WriteServicePrincipal("ci", new ServicePrincipal
        {
            ClientId = "app", TenantId = "ten", Auth = "certificate", CertificatePath = cert,
        });

        var az = new FakeAzRunner();
        new LoginHandler(t.Store, az, TextWriter.Null)
            .Run("ci", new InteractiveLogin(null, false), null, reset: false);

        var args = az.Calls.Single().Args;
        Assert.Contains("--certificate", args);
        Assert.Contains(cert, args);
        Assert.DoesNotContain("-p", args);
    }

    [Fact]
    public void Login_with_new_secret_rotates_the_stored_value()
    {
        using var t = new TempHome();
        t.Store.Create("ci", null, null);
        t.Store.WriteServicePrincipal("ci", Secret(secret: "old"));

        new LoginHandler(t.Store, new FakeAzRunner(), TextWriter.Null)
            .Run("ci", new InteractiveLogin(null, false), Secret(secret: "new"), reset: false);

        Assert.Equal("new", t.Store.ReadServicePrincipal("ci")!.Secret);
    }

    [Fact]
    public void Ls_marks_service_principal_profiles()
    {
        using var t = new TempHome();
        t.Store.Create("ci", null, null);
        t.Store.WriteServicePrincipal("ci", Secret());
        t.WriteAzureProfile("ci", "app-1", "ten-1", "Sub");

        var sw = new StringWriter();
        new LsHandler(t.Store, sw).Run(json: false);

        Assert.Contains("ci (sp)", sw.ToString());
    }

    [Fact]
    public void Write_read_round_trips_the_secret_on_every_platform()
    {
        using var t = new TempHome();
        t.Store.Create("ci", null, null);
        var sp = Secret(secret: "correct horse battery staple");

        t.Store.WriteServicePrincipal("ci", sp);

        Assert.Equal("correct horse battery staple", t.Store.ReadServicePrincipal("ci")!.Secret);
        Assert.Equal("correct horse battery staple", sp.Secret); // caller's object untouched
    }

    [Fact]
    public void On_windows_the_plaintext_secret_is_not_on_disk()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var t = new TempHome();
        t.Store.Create("ci", null, null);
        t.Store.WriteServicePrincipal("ci", Secret(secret: "SUPERSECRET-VALUE-123"));

        var raw = File.ReadAllText(t.Store.SpPath("ci"));
        Assert.DoesNotContain("SUPERSECRET-VALUE-123", raw);
        Assert.Contains("secretProtected", raw);
    }

    [Fact]
    public void A_pre_dpapi_plaintext_sp_json_still_reads()
    {
        using var t = new TempHome();
        t.Store.Create("ci", null, null);
        File.WriteAllText(t.Store.SpPath("ci"),
            "{\"clientId\":\"app\",\"tenantId\":\"ten\",\"auth\":\"secret\",\"secret\":\"legacy-plain\"}");

        Assert.Equal("legacy-plain", t.Store.ReadServicePrincipal("ci")!.Secret);
    }
}
