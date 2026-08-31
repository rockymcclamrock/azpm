using System.Text;
using System.Text.Json;
using Azpm.Mcp;

namespace Azpm.Handlers;

/// <summary><c>azpm mcp</c> — a read-only MCP server exposing the profiles and read-only <c>az</c>.</summary>
public sealed class McpHandler(ProfileStore store, IAzRunner az, string version)
{
    public int Run(TextReader input, TextWriter output)
    {
        new McpServer([ListProfilesTool(), AzTool()], version).Run(input, output);
        return ExitCode.Ok;
    }

    private McpTool ListProfilesTool() => new(
        "azpm_list_profiles",
        "List the azpm Azure profiles (tab-separated: marker, name, account, tenant, subscription, status). "
            + "A '*' marks the profile active in azpm's own shell.",
        """{"type":"object","properties":{},"additionalProperties":false}""",
        _ =>
        {
            var current = Environment.GetEnvironmentVariable(ProfileEnv.Profile);
            var sb = new StringBuilder();
            foreach (var p in store.LoadAll())
            {
                var s = p.ActiveSubscription;
                sb.AppendLine(string.Join('\t',
                    p.Name == current ? "*" : "",
                    p.Name,
                    s?.User?.Name ?? "-",
                    s?.TenantDefaultDomain ?? s?.TenantId ?? "-",
                    s?.Name ?? "-",
                    p.Status));
            }
            return new McpToolResult(sb.Length == 0 ? "(no profiles)" : sb.ToString().TrimEnd());
        });

    private McpTool AzTool() => new(
        "azpm_az",
        "Run a read-only Azure CLI command inside a profile and return its output. " + AzReadOnly.Explain(),
        """
        {
          "type": "object",
          "properties": {
            "profile": { "type": "string", "description": "azpm profile name (see azpm_list_profiles)" },
            "command": {
              "type": "array",
              "items": { "type": "string" },
              "description": "az arguments without the leading 'az', e.g. [\"group\",\"list\",\"-o\",\"table\"]"
            }
          },
          "required": ["profile", "command"],
          "additionalProperties": false
        }
        """,
        args =>
        {
            var profileName = args.ValueKind == JsonValueKind.Object
                && args.TryGetProperty("profile", out var pn) ? pn.GetString() : null;
            if (string.IsNullOrWhiteSpace(profileName))
                return new McpToolResult("missing 'profile'", IsError: true);

            var command = args.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().Select(e => e.GetString() ?? "").Where(x => x.Length > 0).ToList()
                : [];
            if (command.Count == 0)
                return new McpToolResult("missing 'command'", IsError: true);

            if (!AzReadOnly.IsAllowed(command))
                return new McpToolResult(
                    $"refused: 'az {string.Join(' ', command)}' is not read-only. {AzReadOnly.Explain()}",
                    IsError: true);

            Profile profile;
            try
            {
                profile = store.Load(profileName);
            }
            catch (AzpmException ex)
            {
                return new McpToolResult(ex.Message, IsError: true);
            }

            var r = az.Capture(profile.ConfigDir, [.. command, "--only-show-errors"], TimeSpan.FromSeconds(60));
            if (r.TimedOut)
                return new McpToolResult("az timed out", IsError: true);

            if (r.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr;
                return new McpToolResult(
                    $"az exited {r.ExitCode}\n{detail.TrimEnd()}".TrimEnd(), IsError: true);
            }

            return new McpToolResult(
                string.IsNullOrWhiteSpace(r.StdOut) ? "(no output)" : r.StdOut.TrimEnd());
        });
}
