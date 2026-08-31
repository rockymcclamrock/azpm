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

## `azpm mcp`  /  `azpm mcp hide <name>`  /  `azpm mcp show <name>`

`azpm mcp` runs a read-only [MCP](https://modelcontextprotocol.io) server on stdio: an agent gets
`azpm_list_profiles` and `azpm_az` (read-only `az` in a named profile).

`azpm mcp hide <name>` removes a profile from the server entirely — absent from
`azpm_list_profiles`, and `azpm_az` refuses it. `azpm mcp show <name>` undoes it. `azpm ls` marks
hidden profiles `(mcp:hidden)`.

See [docs/mcp.md](mcp.md) for the read-only enforcement details.

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

Table of every profile: name, account, tenant, active subscription, status, and `LOGIN` (how
long since that profile last authenticated). Marks the current `AZPM_PROFILE` with `*` and
service principals with `(sp)`.

Status is `ready` / `logged out` from local files (fast). With `--check` it actually asks `az`
for a token per profile (`valid` / `needs login` / `check timed out`) — a few seconds each.

`LOGIN` is how stale your sign-in is — azpm's own record of the last `add` / `login` / `import`
for that profile. It is **not** a hard expiry: azpm can't see your org's Conditional Access
sign-in-frequency policy, which is what actually decides when you re-MFA (`az` never exposes it,
and the token cache is encrypted at rest). Treat it as "roughly how old is this session" and
compare against whatever cadence your tenants enforce. Profiles last authenticated before this
column existed show `-` until their next login.

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
- **A name that doesn't exist yet:** azpm creates the Chromium profile directory, pre-names it
  (via a `Preferences` file) so it shows as e.g. `g5-prod` in the browser's profile menu, and
  asks the browser to open it. **Sign that profile into the right account once** — a brand-new
  profile has no session, so the first visit goes through a full login. One browser profile per
  azpm profile, one account each. (Firefox: opens its profile manager instead.)
- **Managed / corporate browser:** if the browser is locked down by group policy it may refuse
  the new profile (or fight it with device SSO), and the portal opens in your current profile
  instead. `azpm portal` warns when it can detect this. Workarounds: create the profile in the
  browser UI yourself and sign it in, then `azpm portal <name> --browser-profile "<its name>"`;
  or point `--browser` at a browser that *isn't* managed (often Brave); or `--browser default`.
- With no binding, `azpm portal` uses your OS default browser (no isolation) and prints how to
  bind one.

The portal URL is just `https://portal.azure.com/#@<tenant>` — the tenant is pinned in the URL,
the **account comes from whichever browser profile you're in**. A profile with more than one
account signed in will still show a picker; one account per profile removes it.

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

## `azpm local [<name>] [--allow] [--unset]`  +  `azpm init <shell> --auto`

Per-directory profiles, like `.nvmrc`. `azpm local prod` writes a `.azpm` file (one line: the
profile name) in the current directory; it applies to that directory and everything under it.

```
cd ~/work/acme && azpm local prod      # writes ~/work/acme/.azpm (and trusts it)
azpm local                             # show the profile resolved for the cwd + trust state
azpm local --allow                     # trust an existing .azpm you didn't write here
azpm local --unset                     # remove ./.azpm and its trust entry
```

Add `--auto` to `init` and the shell follows `.azpm` files as you `cd` (nvm/direnv style):

```bash
eval "$(azpm init bash --auto)"
```

Auto-switched profiles are tracked in `AZPM_AUTO`, so a manual `azpm use` in the same tree
isn't clobbered — but leaving a tree with no `.azpm` clears an auto-set profile.

### Trust

A `.azpm` file checked into a repo can name **any** profile you already have, so cloning a repo
and `cd`-ing into it could otherwise switch your live Azure identity with no prompt. `--auto`
therefore only follows a `.azpm` you've **approved**:

- `azpm local <name>` approves the file it writes (the common case needs nothing extra).
- `azpm local --allow` approves the `.azpm` in the current directory.
- Editing an approved `.azpm` revokes trust until you `--allow` it again.
- Approvals live in `~/.azpm/trust.json` (absolute path → content hash).

An unapproved `.azpm` makes the hook print a one-time notice and not switch. If you'd rather skip
this entirely, use `azpm init <shell> --fullauto` — same as `--auto` but with no trust check
(follows any `.azpm`). The invalid-name check still applies in both modes.
