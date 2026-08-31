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
        "test-connection", "register", "unregister", "elevate", "elevate-access", "renew-cert",
        "regenerate-key", "add-ids", "remove-ids", "wipe",
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

        // `az rest` can issue any HTTP verb — only allow it for GET/HEAD.
        if (string.Equals(tokens[0], "rest", StringComparison.OrdinalIgnoreCase))
        {
            var method = OptionValue(command, "--method", "-m") ?? "get";
            return method.Equals("get", StringComparison.OrdinalIgnoreCase)
                || method.Equals("head", StringComparison.OrdinalIgnoreCase);
        }

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
        "update, set, login, get-access-token and similar are rejected. 'az rest' is GET/HEAD only.";

    /// <summary>Finds <c>--name value</c>, <c>--name=value</c>, or a short alias, in the raw args.</summary>
    private static string? OptionValue(IReadOnlyList<string> args, string name, string alias)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return a[(name.Length + 1)..];
            if ((a.Equals(name, StringComparison.OrdinalIgnoreCase)
                 || a.Equals(alias, StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Count)
                return args[i + 1];
        }
        return null;
    }
}
