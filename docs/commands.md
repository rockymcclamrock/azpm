# azpm commands

Global: `--home <dir>` overrides the azpm home (default `AZPM_HOME`, else `~/.azpm` /
`%USERPROFILE%\.azpm`). `--version`, `--help`.

Each profile is a directory `~/.azpm/profiles/<name>/` holding `config/` (the Azure CLI's
`AZURE_CONFIG_DIR` for that profile) and `meta.json` (azpm's own notes).

**Environment set for a profile** (`exec`, `shell`, `use`): `AZURE_CONFIG_DIR`, `AZPM_PROFILE`,
`AZPM_HOME` always; and when the profile is logged in, `ARM_SUBSCRIPTION_ID` / `ARM_TENANT_ID`
(Terraform azurerm) and `AZURE_SUBSCRIPTION_ID` — taken from the profile's active subscription.

| Exit code | Meaning |
|---|---|
| 0 | ok |
| 1 | usage error |
| 2 | profile not found |
| 3 | `az` not found |
| 4 | `az` (or an `exec` command) failed |

---

## `azpm mcp`

Runs a read-only [MCP](https://modelcontextprotocol.io) server on stdio: an agent gets
`azpm_list_profiles` and `azpm_az` (read-only `az` in a named profile). See
[docs/mcp.md](mcp.md).

## `azpm` (no arguments)

In a terminal: a numbered list of your profiles — pick one and it opens that profile's shell
(same as `azpm shell <name>`). Piped or non-interactive: prints the profile table and points at
`--help`.

## `azpm add <name> [--tenant <id/domain>] [--device-code] [--description <text>]`

Creates `profiles/<name>/` and runs `az login` into it. Fails if the profile exists (use
`login`). A failed first login removes the profile.

**Service principal:** add `--service-principal` (`--sp`), `--client-id <appId>`,
`--tenant <id>`, and one of `--client-secret <s>` / `--client-secret-stdin` / `--certificate
<pem>`. The credential is stored at `~/.azpm/profiles/<name>/sp.json` (plaintext, `chmod 600`
on POSIX — OS-keychain storage is [#9](https://github.com/rockymcclamrock/azpm/issues/9)).
`azpm ls` marks these `(sp)`.

## `azpm ls` / `azpm list` `[--json] [--check]`

Table of every profile: name, account, tenant, active subscription, status. Marks the current
`AZPM_PROFILE` with `*` and service principals with `(sp)`.

Status is `ready` / `logged out` from local files (fast). With `--check` it actually asks `az`
for a token per profile (`valid` / `needs login` / `check timed out`) — a few seconds each.

## `azpm path <name>`

Prints the profile's `AZURE_CONFIG_DIR`. Handy for one-offs:
`$env:AZURE_CONFIG_DIR = azpm path prod`.

## `azpm current`

Prints the active profile (`AZPM_PROFILE`); exits non-zero if none.

## `azpm prompt [--format '<tmpl>']`

Prompt-friendly variant: prints the active profile (`{}` in `--format` is the name), **nothing**
when none, always exits 0. Env-only, safe to call every prompt. Recipes: [docs/prompt.md](prompt.md).

## `azpm exec <name> -- <cmd> [args...]`

Runs one command with the profile's environment set (`AZURE_CONFIG_DIR`, `AZPM_PROFILE`,
`AZPM_HOME`). stdio is inherited; the command's exit code is propagated. Everything after `--`
is passed through verbatim.

```
azpm exec prod -- az group list -o table
azpm exec dev  -- terraform apply
```

## `azpm shell <name> [--shell pwsh|powershell|cmd|bash|zsh|fish]`

Opens an interactive subshell with the profile active and a `[azpm:<name>]` prompt prefix (your
existing prompt is preserved). `exit` to leave. The shell is detected from `--shell`, then the
parent process, then `$SHELL`, then the platform default.

## `azpm login <name> [--tenant <id/domain>] [--device-code] [--reset]`

Re-runs `az login` in an existing profile. `--reset` clears the profile's Azure state first —
use it if you need to switch the profile to a different account (plain `az login` *adds* an
account rather than replacing it).

For a service-principal profile, `azpm login <name>` re-auths silently from the stored
`sp.json`. Pass `--client-secret <s>` (or `--client-secret-stdin`) to rotate the stored secret.

## `azpm logout <name>`

Runs `az logout` in the profile. The profile directory and `meta.json` stay; status becomes
`logged out`.

## `azpm rm <name> [--yes]` / `azpm remove …`

Deletes the profile directory. Prompts `[y/N]` unless `--yes`.

## `azpm import <name> [--from <dir>]`

Copies an existing Azure CLI config dir into a new profile — your current login becomes a
profile with no re-auth. `--from` defaults to the active `AZURE_CONFIG_DIR` if set (and outside
`~/.azpm`), else `~/.azure`.

## `azpm rename <old> <new>`

Renames a profile directory and updates its `meta.json`. If the current shell points at `<old>`,
run `azpm use <new>` afterwards.

## `azpm portal <name> [path] [--browser <b>] [--browser-profile <p>]`  ·  `azpm portal --browsers`

Opens `https://portal.azure.com/#@<tenant>` (the profile's active-subscription tenant, plus an
optional blade `path`) in a **browser profile** bound to this azpm profile. Browser profiles
keep their own cookies, so you sign in there once.

```
azpm portal --browsers                                       # list the browser profiles azpm can see
azpm portal prod --browser brave --browser-profile g5-prod   # bind (persists) + open
azpm portal prod                                             # reuse the binding
azpm portal prod /resource/subscriptions                     # deep-link
```

- `--browser`: `edge` | `chrome` | `brave` | `firefox` | `default`.
- `--browser-profile`: for Chromium browsers, either the profile **directory** (`Profile 4`) or
  the **name you see in the browser** (`g5-prod`) — `azpm portal --browsers` shows both.
  Firefox takes the profile name from `about:profiles`.
- **A name that doesn't exist yet:** azpm creates the Chromium profile directory and pre-names
  it (via a `Preferences` file) so it shows up as e.g. `g5-prod` in the browser's profile menu,
  not "Person 3". The browser opens it; sign in once and you're set. This is the recommended
  pattern: one browser profile per azpm profile, one account each. (Firefox: opens its profile
  manager instead — create the profile there.)
- With no binding, `azpm portal` uses your OS default browser (no isolation) and prints how to
  bind one.

**Still seeing an account picker?** The URL pins the tenant and passes a `login_hint`, but a
browser profile with **more than one account signed in** still shows it. One account per browser
profile is what removes it.

## `azpm use <name> [--shell <s>]`  +  `azpm init <shell>`

In-place switching for the *current* shell. `azpm use` on its own only **prints** the env
assignments — a program can't change its parent shell — so it needs a one-time hook, exactly
like `nvm` / `direnv` / `starship init`. (No setup? Use `azpm shell <name>` instead.)

One-time setup — add to your shell profile:

```powershell
# PowerShell  ($PROFILE)
azpm init powershell | Out-String | Invoke-Expression
```
```bash
# bash / zsh  (~/.bashrc, ~/.zshrc)
eval "$(azpm init bash)"
```
```fish
# fish  (~/.config/fish/config.fish)
azpm init fish | source
```

Then:

```
azpm use prod         # sets AZURE_CONFIG_DIR + AZPM_PROFILE in this shell
azpm deactivate       # clears them (back to the default ~/.azure)
```

Without the `init` wrapper, `azpm use <name>` just prints the script — pipe it to your shell's
eval yourself. `cmd` has no wrapper; use `azpm shell` there.

## `azpm local [<name>] [--unset]`  +  `azpm init <shell> --auto`

Per-directory profiles, like `.nvmrc`. `azpm local prod` writes a `.azpm` file (one line: the
profile name) in the current directory; it applies to that directory and everything under it.

```
cd ~/work/acme && azpm local prod      # writes ~/work/acme/.azpm
azpm local                             # show the profile resolved for the cwd
azpm local --unset                     # remove ./.azpm
```

Add `--auto` to `init` and the shell follows `.azpm` files as you `cd` (nvm/direnv style):

```bash
eval "$(azpm init bash --auto)"
```

Auto-switched profiles are tracked in `AZPM_AUTO`, so a manual `azpm use` in the same tree
isn't clobbered — but leaving a tree with no `.azpm` clears an auto-set profile.
