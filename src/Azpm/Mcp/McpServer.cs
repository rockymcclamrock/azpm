using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Azpm.Mcp;

public sealed record McpToolResult(string Text, bool IsError = false);

public sealed class McpTool(
    string name, string description, string inputSchemaJson, Func<JsonElement, McpToolResult> invoke)
{
    public string Name => name;
    public string Description => description;
    public string InputSchemaJson => inputSchemaJson;
    public McpToolResult Invoke(JsonElement args) => invoke(args);
}

/// <summary>
/// A minimal Model Context Protocol server over newline-delimited JSON-RPC on stdio. Handles
/// <c>initialize</c>, <c>ping</c>, <c>tools/list</c>, <c>tools/call</c>. No dependencies.
/// </summary>
public sealed class McpServer(IReadOnlyList<McpTool> tools, string serverVersion)
{
    private const string ProtocolVersion = "2025-06-18";

    public void Run(TextReader input, TextWriter output)
    {
        while (input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                Send(output, Error(null, -32700, "parse error"));
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;
                var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
                JsonElement? id = root.TryGetProperty("id", out var idEl) ? idEl : null;

                if (method is null || id is null)
                    continue; // a response or a notification — nothing to reply to

                var reply = Dispatch(method, root, id.Value);
                if (reply is not null)
                    Send(output, reply);
            }
        }
    }

    private string? Dispatch(string method, JsonElement root, JsonElement id) => method switch
    {
        "initialize" => Result(id, w =>
        {
            w.WriteString("protocolVersion", ProtocolVersion);
            w.WritePropertyName("capabilities");
            w.WriteStartObject();
            w.WritePropertyName("tools");
            w.WriteStartObject();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WritePropertyName("serverInfo");
            w.WriteStartObject();
            w.WriteString("name", "azpm");
            w.WriteString("version", serverVersion);
            w.WriteEndObject();
        }),

        "ping" => Result(id, _ => { }),

        "tools/list" => Result(id, w =>
        {
            w.WritePropertyName("tools");
            w.WriteStartArray();
            foreach (var t in tools)
            {
                w.WriteStartObject();
                w.WriteString("name", t.Name);
                w.WriteString("description", t.Description);
                w.WritePropertyName("inputSchema");
                using var schema = JsonDocument.Parse(t.InputSchemaJson);
                schema.RootElement.WriteTo(w);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }),

        "tools/call" => HandleToolCall(root, id),

        _ => Error(id, -32601, $"method not found: {method}"),
    };

    private string HandleToolCall(JsonElement root, JsonElement id)
    {
        if (!root.TryGetProperty("params", out var p) ||
            !p.TryGetProperty("name", out var nameEl) || nameEl.GetString() is not { } toolName)
            return Error(id, -32602, "missing params.name");

        var tool = tools.FirstOrDefault(t => t.Name == toolName);
        if (tool is null)
            return Error(id, -32602, $"unknown tool: {toolName}");

        var argsEl = p.TryGetProperty("arguments", out var a) ? a : default;
        McpToolResult res;
        try
        {
            res = tool.Invoke(argsEl);
        }
        catch (Exception ex)
        {
            res = new McpToolResult($"error: {ex.Message}", IsError: true);
        }

        return Result(id, w =>
        {
            w.WritePropertyName("content");
            w.WriteStartArray();
            w.WriteStartObject();
            w.WriteString("type", "text");
            w.WriteString("text", res.Text);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteBoolean("isError", res.IsError);
        });
    }

    private static string Result(JsonElement id, Action<Utf8JsonWriter> writeResultBody)
    {
        var buf = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buf))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WritePropertyName("id");
            id.WriteTo(w);
            w.WritePropertyName("result");
            w.WriteStartObject();
            writeResultBody(w);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    private static string Error(JsonElement? id, int code, string message)
    {
        var buf = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buf))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WritePropertyName("id");
            if (id is { } i)
                i.WriteTo(w);
            else
                w.WriteNullValue();
            w.WritePropertyName("error");
            w.WriteStartObject();
            w.WriteNumber("code", code);
            w.WriteString("message", message);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    private static void Send(TextWriter output, string json)
    {
        output.Write(json);
        output.Write('\n');
        output.Flush();
    }
}
