namespace Azpm.Mcp;

/// <summary>
/// Decides whether an <c>az</c> command is read-only enough to expose over the (read-only) MCP
/// server. Allowlist first (must contain a read action), then a mutating-verb blocklist as a
/// second guard.
/// </summary>
public static class AzReadOnly
{
    private static readonly HashSet<string> ReadActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "list", "show", "describe", "get", "check", "search", "history", "diff", "preview",
    };

    private static readonly HashSet<string> Mutating = new(StringComparer.OrdinalIgnoreCase)
    {
        "create", "delete", "update", "set", "add", "remove", "start", "stop", "restart",
        "deploy", "invoke", "run", "exec", "execute", "purge", "regenerate", "renew", "reset",
        "move", "cancel", "approve", "reject", "enable", "disable", "attach", "detach", "grant",
        "revoke", "assign", "unassign", "upload", "publish", "sync", "scale", "migrate",
        "failover", "promote", "rotate", "import", "install", "uninstall", "apply", "destroy",
        "login", "logout", "wait", "new", "init", "up", "down", "generate", "connect",
        "disconnect", "activate", "deactivate", "lock", "unlock", "repair", "restore", "swap",
        "bind", "unbind", "extend", "acquire", "release", "flush", "clear", "kill", "send",
        "test-connection",
    };

    // read-shaped verbs that nonetheless write outside the profile — keep them out.
    private static readonly HashSet<string> BlockedExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "get-access-token", "get-credentials", "get-access-credentials", "download",
        "export", "get-secrets", "list-keys", "show-connection-string",
    };

    public static bool IsAllowed(IReadOnlyList<string> command)
    {
        var tokens = command.Where(t => !t.StartsWith('-')).Select(t => t.Trim()).ToList();
        if (tokens.Count == 0)
            return false;

        var sawRead = false;
        foreach (var tok in tokens)
        {
            if (BlockedExact.Contains(tok) || Mutating.Contains(tok))
                return false;
            if (ReadActions.Contains(tok) ||
                tok.StartsWith("list-", StringComparison.OrdinalIgnoreCase) ||
                tok.StartsWith("show-", StringComparison.OrdinalIgnoreCase))
                sawRead = true;
        }

        // `az account`, `az group` on their own list; `az version` is fine.
        if (!sawRead && tokens is [var only] && only is "account" or "version")
            return true;

        return sawRead;
    }

    public static string Explain() =>
        "read-only: the command must be a list/show/describe-style query. create, delete, " +
        "update, set, login, get-access-token and similar are rejected.";
}
