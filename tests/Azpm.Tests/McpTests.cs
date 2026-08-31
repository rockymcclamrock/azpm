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
    public void Rejects_mutations_and_secret_reads(params string[] cmd) =>
        Assert.False(AzReadOnly.IsAllowed(cmd));
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
}
