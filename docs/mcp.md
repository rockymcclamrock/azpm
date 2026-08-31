# `azpm mcp` — read-only MCP server

`azpm mcp` speaks the [Model Context Protocol](https://modelcontextprotocol.io) over stdio, so an
agent (Claude, etc.) can inspect your Azure through your profiles **without you switching
context** — and without being able to change anything.

## Tools

| Tool | Does |
|---|---|
| `azpm_list_profiles` | Lists your profiles: name, account, tenant, active subscription, status. |
| `azpm_az` | Runs a **read-only** `az` command in a named profile. `{ "profile": "prod", "command": ["group","list","-o","table"] }` |

**Read-only enforcement:** `azpm_az` only runs list / show / describe-style commands. Anything
matching `create`, `delete`, `update`, `set`, `restart`, `login`, `get-access-token`,
`get-credentials`, `download`, … is refused before `az` is invoked. It is still your Azure
access, scoped to whatever the profile can see — only expose profiles you're comfortable letting
an agent read.

## Wiring it up

**Claude Code:**

```
claude mcp add azpm -- azpm mcp
```

**Generic MCP client** (`mcp.json` style):

```json
{
  "mcpServers": {
    "azpm": { "command": "azpm", "args": ["mcp"] }
  }
}
```

The server exits when stdin closes.
