namespace Azpm.Mcp;

/// <summary>
/// Decides whether an <c>az</c> command is safe to expose over the read-only MCP server.
///
/// "Read-only" here means <b>cannot change anything and cannot read live credential material</b>.
/// Non-mutating is not sufficient: <c>az keyvault secret show</c>, <c>az storage account keys
/// list</c>, <c>az acr credential show</c> etc. return secrets while changing nothing, so they are
/// refused too. See docs/mcp.md for the rationale and the (deliberate) absence of an override.
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

    // Diagnostic flags that make `az` dump the HTTP pipeline (Authorization: Bearer …) and MSAL
    // logs to stderr, which `azpm_az` returns to the caller. Never over MCP.
    private static readonly HashSet<string> BlockedFlags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--debug", "--verbose",
    };

    public static bool IsAllowed(IReadOnlyList<string> command)
    {
        foreach (var arg in command)
        {
            var eq = arg.IndexOf('=');
            var flag = eq >= 0 ? arg[..eq] : arg;
            if (BlockedFlags.Contains(flag))
                return false;
        }

        var tokens = command.Where(t => !t.StartsWith('-')).Select(t => t.Trim()).ToList();
        if (tokens.Count == 0)
            return false;

        if (string.Equals(tokens[0], "rest", StringComparison.OrdinalIgnoreCase))
            return IsAllowedRest(command);

        if (IsSecretSurface(tokens))
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

    /// <summary>
    /// <c>az rest</c> is an arbitrary HTTP client. Allow it only as GET/HEAD, with no request body,
    /// against the ARM management plane — which keeps Key Vault data-plane, Microsoft Graph, and
    /// storage endpoints (all secret- or PII-bearing) out of reach.
    /// </summary>
    private static bool IsAllowedRest(IReadOnlyList<string> command)
    {
        var method = OptionValue(command, "--method", "-m") ?? "get";
        if (!method.Equals("get", StringComparison.OrdinalIgnoreCase)
            && !method.Equals("head", StringComparison.OrdinalIgnoreCase))
            return false;

        if (OptionValue(command, "--body", "-b") is not null)
            return false;

        var url = OptionValue(command, "--url", "-u")
            ?? OptionValue(command, "--uri", null)
            ?? OptionValue(command, "--uri-parameters", null); // not a URL, but be conservative
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // az resolves a leading-slash URL against https://management.azure.com.
        if (url.StartsWith('/'))
            return true;

        return url.Contains("://management.azure.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Non-mutating commands that hand back live secrets / keys / passwords.</summary>
    private static bool IsSecretSurface(IReadOnlyList<string> tokens)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            var tok = tokens[i].ToLowerInvariant();
            var next = i + 1 < tokens.Count ? tokens[i + 1].ToLowerInvariant() : "";

            if (tok == "keys" && next == "list") return true;           // storage/cosmosdb/functionapp keys list
            if (tok == "list" && next == "keys") return true;
            if (tok.StartsWith("list-publishing")) return true;         // webapp deployment list-publishing-profiles
            if (tok.Contains("connection-string")) return true;         // *config connection-string list
            if ((tok == "credential" || tok == "credentials") && next == "show") return true; // acr credential show

            if (tok == "keyvault")
            {
                var rest = new List<string>();
                for (var j = i + 1; j < tokens.Count; j++)
                    rest.Add(tokens[j].ToLowerInvariant());
                var material = rest.Contains("secret") || rest.Contains("key") || rest.Contains("certificate");
                var leaf = rest.Contains("show") || rest.Contains("download")
                    || rest.Contains("backup") || rest.Contains("show-deleted");
                if (material && leaf) return true;
            }
        }
        return false;
    }

    public static string Explain() =>
        "read-only: the command must be a list/show/describe-style query that neither changes "
        + "state nor returns secrets. Rejected: create/delete/update/set/login/get-access-token "
        + "and similar; secret reads (keyvault secret/key show, '* keys list', 'credential show', "
        + "connection-string, list-publishing-*); the --debug/--verbose flags. 'az rest' is "
        + "GET/HEAD only, no body, ARM management-plane URLs only.";

    /// <summary>Finds <c>--name value</c>, <c>--name=value</c>, or a short alias, in the raw args.</summary>
    private static string? OptionValue(IReadOnlyList<string> args, string name, string? alias)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return a[(name.Length + 1)..];
            if ((a.Equals(name, StringComparison.OrdinalIgnoreCase)
                 || (alias is not null && a.Equals(alias, StringComparison.OrdinalIgnoreCase)))
                && i + 1 < args.Count)
                return args[i + 1];
        }
        return null;
    }
}
