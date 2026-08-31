using System.Text.Json;
using Azpm;
using Azpm.Handlers;
using Azpm.Mcp;
using Xunit;

namespace Azpm.Tests;

public sealed class AzReadOnlyTests
{
    [Theory]
    [InlineData("account", "show")]
    [InlineData("group", "list")]
    [InlineData("vm", "list", "-o", "table")]
    [InlineData("resource", "show", "--ids", "x")]
    [InlineData("monitor", "metrics", "list")]
    [InlineData("role", "assignment", "list")]
    [InlineData("account")]
    [InlineData("version")]
    [InlineData("storage", "account", "list")]
    public void Allows_read_queries(params string[] cmd) =>
        Assert.True(AzReadOnly.IsAllowed(cmd));

    [Theory]
    [InlineData("group", "delete", "-n", "x")]
    [InlineData("vm", "create")]
    [InlineData("keyvault", "secret", "set")]
    [InlineData("webapp", "restart")]
    [InlineData("account", "get-access-token")]
    [InlineData("aks", "get-credentials", "-n", "x")]
    [InlineData("storage", "blob", "download")]
    [InlineData("login")]
    [InlineData("ad", "sp", "create-for-rbac")]
    [InlineData("group")]                       // no read action, not a bare allowed word
    [InlineData("resource", "update")]
    [InlineData("provider", "register", "-n", "x")]
    [InlineData("rest", "--method", "post", "--url", "https://x")]
    [InlineData("rest", "--method", "PATCH", "--url", "https://x")]
    [InlineData("rest", "-m", "delete", "--url", "https://x")]
    [InlineData("rest", "--method=put", "--url", "https://x")]
    public void Rejects_mutations_and_secret_reads(params string[] cmd) =>
        Assert.False(AzReadOnly.IsAllowed(cmd));

    [Theory]
    // #11 — diagnostic flags that dump bearer tokens / MSAL logs to stderr
    [InlineData("account", "show", "--debug")]
    [InlineData("group", "list", "--verbose")]
    [InlineData("rest", "--url", "/subscriptions", "--debug")]
    [InlineData("account", "show", "--debug=true")]
    // #12 — non-mutating, but hand back live secrets
    [InlineData("keyvault", "secret", "show", "--vault-name", "v", "-n", "s")]
    [InlineData("keyvault", "key", "download", "--vault-name", "v", "-n", "k", "-f", "k.pem")]
    [InlineData("keyvault", "certificate", "backup", "--vault-name", "v", "-n", "c")]
    [InlineData("storage", "account", "keys", "list", "-n", "sa")]
    [InlineData("cosmosdb", "keys", "list", "-n", "db", "-g", "rg")]
    [InlineData("acr", "credential", "show", "-n", "reg")]
    [InlineData("functionapp", "config", "connection-string", "list", "-n", "fa", "-g", "rg")]
    [InlineData("webapp", "deployment", "list-publishing-profiles", "-n", "wa", "-g", "rg")]
    // #12 — az rest reaching non-ARM (secret / PII) planes
    [InlineData("rest", "--url", "https://myvault.vault.azure.net/secrets/x?api-version=7.4")]
    [InlineData("rest", "--url", "https://graph.microsoft.com/v1.0/users")]
    [InlineData("rest", "--method", "get", "--url", "https://management.azure.com/x", "--body", "{}")]
    public void Rejects_diagnostic_flags_and_secret_surface(params string[] cmd) =>
        Assert.False(AzReadOnly.IsAllowed(cmd));

    [Theory]
    [InlineData("rest", "--url", "https://management.azure.com/x")]        // defaults to GET
    [InlineData("rest", "--method", "get", "--url", "https://management.azure.com/x")]
    [InlineData("rest", "-m", "GET", "--url", "/subscriptions")]           // leading slash => ARM
    [InlineData("rest", "--method=head", "--url", "https://management.azure.com/x")]
    [InlineData("keyvault", "secret", "list", "--vault-name", "v")]        // ids only, no values
    [InlineData("keyvault", "show", "-n", "v")]                            // vault metadata
    public void Allows_az_rest_get_and_non_secret_reads(params string[] cmd) =>
        Assert.True(AzReadOnly.IsAllowed(cmd));
}

public sealed class McpOutputTests
{
    [Fact]
    public void Redacts_bearer_tokens_and_access_token_fields()
    {
        var raw = "Authorization: Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.abcdefg\n"
            + "{\"accessToken\": \"eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9payload\"}";
        var clean = McpOutput.Sanitize(raw);

        Assert.DoesNotContain("eyJ0eXAiOiJKV1Qi", clean);
        Assert.Contains("Bearer <redacted>", clean);
        Assert.Contains("\"accessToken\": \"<redacted>\"", clean);
    }

    [Fact]
    public void Caps_oversized_output_with_a_marker()
    {
        var big = new string('x', McpOutput.MaxBytes + 50_000);
        var capped = McpOutput.Sanitize(big);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(capped) <= McpOutput.MaxBytes);
        Assert.Contains("output truncated", capped);
    }

    [Fact]
    public void Leaves_normal_output_untouched()
    {
        const string ok = "[\n  { \"name\": \"rg-dev\", \"location\": \"eastus\" }\n]";
        Assert.Equal(ok, McpOutput.Sanitize(ok));
    }
}

public sealed class McpServerTests
{
    private static List<JsonElement> Exchange(McpServer server, params string[] requests)
    {
        var outw = new StringWriter();
        server.Run(new StringReader(string.Join('\n', requests) + "\n"), outw);
        return outw.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .ToList();
    }

    private static McpServer Bare(params McpTool[] tools) => new(tools, "9.9.9");

    [Fact]
    public void Initialize_reports_protocol_and_server_info()
    {
        var replies = Exchange(Bare(),
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");

        var result = replies.Single().GetProperty("result");
        Assert.Equal("azpm", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
    }

    [Fact]
    public void Notifications_get_no_reply()
    {
        var replies = Exchange(Bare(),
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        Assert.Empty(replies);
    }

    [Fact]
    public void Tools_list_returns_the_registered_tools_with_schemas()
    {
        var tool = new McpTool("t1", "does a thing",
            """{"type":"object","properties":{}}""", _ => new McpToolResult("ok"));

        var replies = Exchange(Bare(tool),
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");

        var tools = replies.Single().GetProperty("result").GetProperty("tools");
        Assert.Equal("t1", tools[0].GetProperty("name").GetString());
        Assert.Equal("object", tools[0].GetProperty("inputSchema").GetProperty("type").GetString());
    }

    [Fact]
    public void Tools_call_runs_the_tool_and_wraps_the_text()
    {
        var tool = new McpTool("echo", "echo", """{"type":"object"}""",
            args => new McpToolResult("you said: " + args.GetProperty("msg").GetString()));

        var replies = Exchange(Bare(tool),
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo","arguments":{"msg":"hi"}}}""");

        var content = replies.Single().GetProperty("result").GetProperty("content")[0];
        Assert.Equal("text", content.GetProperty("type").GetString());
        Assert.Equal("you said: hi", content.GetProperty("text").GetString());
        Assert.False(replies.Single().GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public void Unknown_method_is_a_jsonrpc_error()
    {
        var replies = Exchange(Bare(),
            """{"jsonrpc":"2.0","id":4,"method":"nope"}""");
        Assert.Equal(-32601, replies.Single().GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void Bad_json_is_a_parse_error()
    {
        var replies = Exchange(Bare(), "{ not json");
        Assert.Equal(-32700, replies.Single().GetProperty("error").GetProperty("code").GetInt32());
    }
}

public sealed class McpHandlerTests
{
    private static (List<JsonElement> replies, FakeAzRunner az) Run(TempHome t, params string[] requests)
    {
        var az = new FakeAzRunner();
        var outw = new StringWriter();
        new McpHandler(t.Store, az, "1.2.3")
            .Run(new StringReader(string.Join('\n', requests) + "\n"), outw);
        var replies = outw.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
        return (replies, az);
    }

    [Fact]
    public void list_profiles_tool_returns_the_profiles()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);
        t.WriteAzureProfile("dev", "dev@x.example.com", "x.example.com", "Dev Sub");

        var (replies, _) = Run(t,
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"azpm_list_profiles","arguments":{}}}""");

        var text = replies.Single().GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("dev", text);
        Assert.Contains("dev@x.example.com", text);
    }

    [Fact]
    public void az_tool_runs_a_read_command_in_the_profile()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);

        var (replies, az) = Run(t,
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"azpm_az","arguments":{"profile":"dev","command":["group","list"]}}}""");

        var call = az.Calls.Single();
        Assert.Equal(t.Home.ConfigDir("dev"), call.ConfigDir);
        Assert.Equal(["group", "list", "--only-show-errors"], call.Args);
        Assert.False(replies.Single().GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public void az_tool_refuses_a_mutation_without_calling_az()
    {
        using var t = new TempHome();
        t.Store.Create("dev", null, null);

        var (replies, az) = Run(t,
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"azpm_az","arguments":{"profile":"dev","command":["group","delete","-n","x"]}}}""");

        Assert.Empty(az.Calls);
        var result = replies.Single().GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("not read-only", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void az_tool_reports_an_unknown_profile()
    {
        using var t = new TempHome();
        var (replies, az) = Run(t,
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"azpm_az","arguments":{"profile":"nope","command":["group","list"]}}}""");

        Assert.Empty(az.Calls);
        Assert.True(replies.Single().GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public void hidden_profiles_are_absent_from_list_profiles_and_refused_by_az()
    {
        using var t = new TempHome();
        t.Store.Create("open", null, null);
        t.WriteAzureProfile("open", "u@a", "a.example.com", "A");
        t.Store.Create("secret", null, null);
        t.WriteAzureProfile("secret", "u@b", "b.example.com", "B");
        t.Store.SetMcpHidden("secret", true);

        var (list, _) = Run(t,
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"azpm_list_profiles","arguments":{}}}""");
        var text = list.Single().GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("open", text);
        Assert.DoesNotContain("secret", text);

        var (call, az) = Run(t,
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"azpm_az","arguments":{"profile":"secret","command":["group","list"]}}}""");
        Assert.Empty(az.Calls);
        var result = call.Single().GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Contains("hidden from MCP", result.GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void visibility_handler_toggles_the_meta_flag()
    {
        using var t = new TempHome();
        t.Store.Create("p", null, null);

        new McpVisibilityHandler(t.Store, TextWriter.Null).Run("p", hide: true);
        Assert.True(t.Store.Load("p").Meta!.McpHidden);

        new McpVisibilityHandler(t.Store, TextWriter.Null).Run("p", hide: false);
        Assert.True(t.Store.Load("p").Meta!.McpHidden is null or false);
    }
}
