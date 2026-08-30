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

## `azpm add <name> [--tenant <id/domain>] [--device-code] [--description <text>]`

Creates `profiles/<name>/` and runs `az login` into it. Fails if the profile exists (use
`login`). A failed first login removes the profile.

**Service principal:** add `--service-principal` (`--sp`), `--client-id <appId>`,
`--tenant <id>`, and one of `--client-secret <s>` / `--client-secret-stdin` / `--certificate
<pem>`. The credential is stored at `~/.azpm/profiles/<name>/sp.json` (plaintext, `chmod 600`
on POSIX — OS-keychain storage is [#9](https://github.com/rockymcclamrock/azpm/issues/9)).
`azpm ls` marks these `(sp)`.

## `azpm ls` / `azpm list` `[--json]`

Table of every profile: name, account, tenant, active subscription, status
(`ready` / `logged out`). Marks the current `AZPM_PROFILE` with `*`.

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

## `azpm portal <name> [path] [--browser <b>] [--browser-profile <p>]`

Opens `https://portal.azure.com/#@<tenant>` (the profile's active-subscription tenant) — plus an
optional blade `path` — in a browser profile bound to this azpm profile. Browser profiles keep
their own cookies, so once you've signed in there it stays signed in.

```
azpm portal prod --browser edge --browser-profile "Profile 2"   # bind (persists to meta.json) + open
azpm portal prod                                                # reuse the binding
azpm portal prod /resource/subscriptions                        # deep-link
```

`--browser`: `edge` | `chrome` | `firefox` | `default`. With no binding it uses the OS default
browser and prints a hint. Firefox uses separate profiles (`-P`), not containers.

## `azpm use <name> [--shell <s>]`  +  `azpm init <shell>`

In-place switching for the *current* shell (no subshell). One-time setup — add to your shell
profile:

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
