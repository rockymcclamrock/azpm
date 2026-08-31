# `azpm mcp` — read-only MCP server

`azpm mcp` speaks the [Model Context Protocol](https://modelcontextprotocol.io) over stdio, so an
agent (Claude, etc.) can inspect your Azure through your profiles **without you switching
context** — and without being able to change anything or read live secrets.

## Tools

| Tool | Does |
|---|---|
| `azpm_list_profiles` | Lists your profiles: name, account, tenant, active subscription, status. |
| `azpm_az` | Runs a **read-only** `az` command in a named profile. `{ "profile": "prod", "command": ["group","list","-o","table"] }` |

## Read-only enforcement

`azpm_az` classifies the command **before `az` is spawned** and refuses anything that isn't a
pure read. "Read-only" here means two things — *cannot change state* **and** *cannot return live
credential material*:

| Refused | Examples |
|---|---|
| Mutations | `create`, `delete`, `update`, `set`, `restart`, `login`, `register`, … |
| Token / credential fetches | `account get-access-token`, `aks get-credentials`, `* get-credentials` |
| Secret reads (non-mutating, but hand back secrets) | `keyvault secret/key show`, `keyvault key download`, `storage account keys list`, `cosmosdb keys list`, `acr credential show`, `webapp config connection-string list`, `webapp deployment list-publishing-profiles` |
| Diagnostic flags | `--debug`, `--verbose` (they dump `Authorization: Bearer …` headers and MSAL logs to stderr, which the tool would otherwise return) |
| `az rest` outside a narrow lane | anything but `GET`/`HEAD`; any request with `--body`; any URL that isn't the ARM management plane (`https://management.azure.com` or a leading-slash path). Key Vault data-plane, Microsoft Graph, and storage endpoints are all unreachable this way. |

Returned output is also scrubbed of stray `Bearer <token>` / `"accessToken": "…"` strings and
capped at 256 KB.

**Why no override.** There is deliberately no flag or env var to relax this. The server exists so
you can point an agent at your Azure *without* having to trust it with secrets; an "allow secrets"
switch would erase that guarantee the first time it's set and forgotten. If a real need for
scoped secret access shows up we'll revisit it as an explicit, separate opt-in — file an issue.

**Still your access.** Everything the agent can read is whatever the profile's identity can see.
Only add profiles you're comfortable letting an agent read broadly (subscriptions, resource
groups, resource configs, role assignments, `az ad` directory data).

## Hiding a profile

```
azpm mcp hide prod      # drop 'prod' from the server entirely
azpm mcp show prod      # undo
```

A hidden profile is absent from `azpm_list_profiles` and `azpm_az` refuses it before spawning
`az`. `azpm ls` marks it `(mcp:hidden)`. Use this for a profile whose identity can see something
you don't want an agent reading, without giving up the MCP server for your other profiles. The
flag lives in that profile's `meta.json`; a fresh `azpm mcp` picks it up (restart the server /
reload your client).

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
