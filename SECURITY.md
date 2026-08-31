# Security

`azpm` brokers Azure sign-in state and ships an MCP server, so it's worth being
explicit about what it does and does not protect.

## Reporting a vulnerability

Use GitHub's **private vulnerability reporting**: the repository's **Security**
tab → **Report a vulnerability**. Include a description and, if you have one, a
proof of concept. Please don't open a regular issue or PR for anything
exploitable — that discloses it publicly before there's a fix.

<!-- maintainer: enable private vulnerability reporting (Settings → Advanced
Security) when this repo goes public; it is not available while the repo is
private. Until then, collaborators should contact the maintainer through GitHub
rather than filing an issue. -->

Expect an acknowledgement within a few days.

## Trust boundaries

### `PATH` is trusted

`azpm` resolves `az`, the browser, and the shell from `PATH` / the registry and
executes them. A writable directory earlier on `PATH` than the real binaries is
game over — the same assumption every developer tool makes. Don't run `azpm`
(or `az`, or your shell) with an attacker-influenced `PATH`.

### `.azpm` files are **untrusted** until approved

A `.azpm` file selects which profile a directory tree uses. Because it can name
any profile you already have, a `.azpm` committed to a repo you clone could
switch your live Azure identity the moment you `cd` in.

`azpm init <shell> --auto` therefore only follows a `.azpm` you've approved
(`azpm local <name>` approves the one it writes; `azpm local --allow` approves an
existing one). Approvals are keyed by absolute path and a content hash in
`~/.azpm/trust.json`, so editing a file re-arms the check.

`azpm init <shell> --fullauto` disables this gate by choice. Profile names from
a `.azpm` are always validated (no path traversal) regardless of mode.

### The MCP client is trusted; the model is not

`azpm mcp` is spawned by your MCP client over stdio — that client is trusted.
The **model** on the other end is treated as potentially adversarial:

- `azpm_az` refuses any command that mutates state **or returns live secrets**
  (`keyvault secret show`, `* keys list`, `acr credential show`,
  `*connection-string* list`, `list-publishing-*`, `get-access-token`,
  `get-credentials`, …).
- The `--debug` / `--verbose` flags are refused (they dump bearer tokens and
  MSAL logs to stderr).
- `az rest` is limited to `GET`/`HEAD`, no request body, ARM management-plane
  URLs only — Key Vault data-plane, Microsoft Graph and storage endpoints are
  unreachable.
- Returned text is scrubbed of stray `Bearer` / `accessToken` strings and
  capped at 256 KB.

There is deliberately no switch to relax this — see
[docs/mcp.md](docs/mcp.md). It's still your access: the model can read whatever
the profile's identity can see (subscriptions, resource configs, role
assignments, `az ad` directory data), so only add profiles you're comfortable
letting an agent read broadly.

## Credential storage

| Credential | Where | Protection |
|---|---|---|
| Interactive user tokens | `~/.azpm/profiles/<name>/config/` — the Azure CLI's own MSAL cache | DPAPI-encrypted on Windows; `0600` JSON on macOS/Linux. `az` owns this, not `azpm`. |
| Service-principal secret | `~/.azpm/profiles/<name>/sp.json` | **Windows:** DPAPI-encrypted (`secretProtected`, per-user, per-machine). **POSIX:** plaintext, `0600`. During `azpm login` it's passed to `az` via a short-lived `-p @file` inside the profile dir, not on the argv. Tracked: [#9](https://github.com/rockymcclamrock/azpm/issues/9) (OS keychain for the POSIX side). |
| `.azpm` trust list | `~/.azpm/trust.json` | Not sensitive (paths + hashes). Integrity matters, not secrecy. |

## What `azpm` never does

- No network calls of its own. Everything that talks to Azure is `az`.
- No telemetry, no analytics, no auto-update.
- No credential extraction or re-vaulting — `az` keeps owning its tokens.

## Scope

Interactive `az` accounts and service principals, Portal browser integration,
per-directory profiles, and the read-only MCP server. Azure PowerShell context
isolation and OS-keychain secret storage are on the roadmap
([PLAN.md](PLAN.md)).
