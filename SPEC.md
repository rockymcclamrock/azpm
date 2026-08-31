# azpm — Azure Profile Manager

**Status:** design overview — problem, decisions, and scope. Behaviour that has shipped is
documented in [docs/commands.md](docs/commands.md); this is the "why".
**Name:** `azpm` (azure profile manager)
**One-liner:** `aws-vault` / `granted` for Azure — named, isolated login profiles you switch between instantly, no re-login.

> Named `azpm` rather than `azp` because `AZP_*` is Azure Pipelines' self-hosted-agent env-var
> namespace (`AZP_URL`, `AZP_TOKEN`, …) and `azp-cli` was already taken.

---

## 1. Problem

Working across multiple Azure tenants (e.g. a dev tenant and a prod tenant, each with its own
user account) is painful with the stock tooling:

- `az login` stores **all** state in one place (`~/.azure`). Logging into tenant B clobbers
  tenant A's context. Switching back means logging in again (browser + MFA).
- `az account set` only switches *subscriptions within the accounts you're currently logged into* —
  it does nothing for multi-tenant / multi-account.
- There is no first-party "profiles" concept. Prior art is blog posts, `AZURE_CONFIG_DIR` shell
  hacks, and one-off scripts. Nothing polished, nothing Windows-first.
- The AWS world solved this years ago (`aws-vault`, `granted`/`assume`). Azure has an
  [open Terraform issue](https://github.com/hashicorp/terraform-provider-azurerm/issues/26577)
  and years of complaints.

### Who feels this

Azure consultants, CSP partners, MSPs, and any team with a dev/prod tenant split. The AWS
equivalents have tens of thousands of users.

### Not the same as the existing tools

[`azctx`](https://github.com/whiteducksoftware/azctx), [`aztx`](https://github.com/riweston/aztx),
`azcx` switch the **active subscription within one login**. `azpm` manages **multiple independent
logins** (different tenants, different accounts) side by side. Different problem.

---

## 2. Core mechanism

The Azure CLI reads **every** piece of login state from the directory named by the
`AZURE_CONFIG_DIR` environment variable (default `~/.azure`):

| File | Contents |
|---|---|
| `azureProfile.json` | Known subscriptions, tenants, the active subscription, account UPNs |
| `msal_token_cache.bin` (Windows, DPAPI-encrypted) / `msal_token_cache.json` (macOS/Linux) | MSAL access + refresh tokens |
| `config` | Per-context `az config` settings |
| `commands`, `logs`, `telemetry`, `cliextensions` | Caches, extensions |

**Point `AZURE_CONFIG_DIR` at a per-profile directory and each profile has completely
independent login state.** No credential extraction, no secret vaulting — MSAL manages its own
token cache inside that directory. That is the entire trick, and it's why v0.1 is small.

`azpm` is, at its core, a manager for a set of these directories plus a comfortable way to run
commands or a shell with the right one selected.

### What this does *not* cover in v0.1

- Azure PowerShell (`Connect-AzAccount`) keeps context in `~/.Azure` (capital A) and
  `AzureRmContext.json` — a **separate** mechanism. Deferred.
- Azure Portal in the browser — needs browser-profile / container integration. Deferred (this is
  the #2 pain and the flagship v0.2 feature).
- VS Code / Storage Explorer — pick up `AZURE_CONFIG_DIR` inconsistently. Out of scope.

---

## 3. Identity model

v0.1 targets **interactive user accounts** (browser login, MFA, one account per tenant). Service
principals (client id + secret/cert, non-interactive) are deferred to v0.2 — they're easy to add
later as an `azpm add --service-principal` path.

---

## 4. Layout on disk

```
$AZPM_HOME/                     # default: %USERPROFILE%\.azpm  (Windows)
                                #          $XDG_DATA_HOME/azpm or ~/.azpm  (macOS/Linux)
  profiles/
    dev/
      config/                   # <- this is the AZURE_CONFIG_DIR for the "dev" profile
        azureProfile.json
        msal_token_cache.bin
        ...
      meta.json                 # azpm's own metadata (see below)
    prod/
      config/
      meta.json
```

`meta.json` (written and owned by `azpm`, never by `az`):

```json
{
  "name": "dev",
  "created": "2026-08-29T21:30:00Z",
  "tenantHint": "contoso-dev.onmicrosoft.com",
  "description": "Contoso dev tenant - my.name@contoso-dev.onmicrosoft.com",
  "lastUsed": "2026-08-29T22:05:00Z"
}
```

There is **no global "active profile" file**. The active profile is per-shell, carried in the
`AZPM_PROFILE` environment variable. This keeps concurrent shells independent and the tool
stateless.

### Name rules

`^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`. Reserved: `current`, `list`, `all`.

---

## 5. Commands (v0.1)

All commands accept `--json` for machine-readable output where it makes sense, and honor
`AZPM_HOME`.

| Command | Behavior |
|---|---|
| `azpm add <name> [--tenant <id/domain>] [--device-code] [--description <text>]` | Creates `profiles/<name>/config/`, sets `AZURE_CONFIG_DIR` to it, runs `az login` (passing `--tenant` / `--use-device-code` through), writes `meta.json`. Fails if the profile exists (use `azpm login` to re-auth). |
| `azpm ls` / `azpm list` | Table: **name**, **account** (UPN), **tenant**, **active subscription**, **status**. Data comes from each profile's `azureProfile.json` + `meta.json`. `status` is `ready` / `logged out` / `needs login` (best-effort; see §7). Marks the current `AZPM_PROFILE` with `*`. |
| `azpm exec <name> -- <cmd> [args...]` | Runs one command with `AZURE_CONFIG_DIR` (and `AZPM_PROFILE`) set in its environment. Inherits stdio, propagates exit code. The workhorse on day one. |
| `azpm shell <name> [--shell pwsh\|bash\|zsh\|fish]` | Spawns an interactive subshell with the profile's env set and a prompt marker (`AZPM_PROFILE` + a `PROMPT`/`PS1` prefix). Exit the subshell to return. Zero config. Detects shell from `--shell`, then parent process, then `$SHELL` / `$env:ComSpec`. |
| `azpm use <name>` | Prints shell code that exports the profile's env into the **current** shell. Only works when wrapped by `azpm init` (otherwise prints a hint). |
| `azpm init <pwsh\|bash\|zsh\|fish>` | Emits the shell function/hook to stdout for `eval` / dot-sourcing in a profile script. Makes `azpm use` and `azpm deactivate` work in-place, nvm-style. |
| `azpm current` | Prints the active profile name (from `AZPM_PROFILE`), or exits non-zero if none. |
| `azpm path <name>` | Prints the absolute `AZURE_CONFIG_DIR` for the profile (scripting / debugging). |
| `azpm login <name> [--tenant ...] [--device-code]` | Re-runs `az login` in an existing profile. |
| `azpm logout <name>` | Runs `az logout` in the profile and clears its token cache. Keeps the profile + `meta.json`. |
| `azpm rm <name> [--yes]` | Deletes the profile directory. Prompts unless `--yes`. |

### Primary UX decision

Ship **both** `azpm shell` and `azpm use`/`azpm init`, and lead the docs with `azpm shell` — it
needs zero setup and is the safest to demo. Power users graduate to the `init` hook for in-place
switching and (v0.2) directory auto-switch.

### Deferred to v0.2+

- Portal browser-profile / container integration (**#2 pain**, flagship feature)
- `.azpm` per-repo file + directory auto-switch (like `.nvmrc`)
- Rich prompt segment / starship-style module
- Service principals (`--service-principal`, cert auth)
- Azure PowerShell context isolation (`AzureRmContext` redirection)
- `ARM_*` / `AZURE_*` export for Terraform / Bicep / SDKs
- Keychain-locked token cache (materialize on use — the `aws-vault` security model)
- TUI picker (`azpm` with no args)
- MCP server (let Claude run `az` in the right context)
- `azpm rename`, `azpm clone`, import from existing `~/.azure`

---

## 6. Cross-cutting behavior

- **Config root resolution:** `--home` flag > `AZPM_HOME` env > `%USERPROFILE%\.azpm` (Windows) /
  `$XDG_DATA_HOME/azpm` else `~/.azpm`.
- **`az` discovery:** find `az` / `az.cmd` on `PATH`; clear error if missing, with install link.
- **Env injected into children:** `AZURE_CONFIG_DIR`, `AZPM_PROFILE`, and (so nested `azpm` calls
  behave) `AZPM_HOME`.
- **Exit codes:** `0` ok, `1` usage error, `2` profile not found, `3` `az` not found,
  `4` `az` command failed (exec/login), `130` interrupted.
- **No telemetry. No network calls of its own** — everything goes through `az`.
- **Windows-first but cross-platform.** PowerShell 7 + Windows Terminal are the primary
  development/test environment.

---

## 7. Known unknowns — spike before building (Phase 0)

1. **Isolation proof.** Set up two real tenants end-to-end via two `AZURE_CONFIG_DIR`s on the dev
   machine. Confirm zero cross-contamination of subscriptions and tokens, and that both stay
   logged in across a reboot (DPAPI cache survives).
2. **`az login` into a fresh dir.** Browser vs `--use-device-code` behavior; what exit code and
   stderr look like on cancel; whether `az login` respects `AZURE_CONFIG_DIR` for *writing* the
   new profile (expected yes).
3. **Reading status for `azpm ls`.** `azureProfile.json` gives account/tenant/subscription
   cheaply. Token validity is harder: the Windows cache is DPAPI-encrypted, so we can't just
   parse expiry. Options: (a) show `ready` if `azureProfile.json` has an account and skip real
   token checks; (b) `az account get-access-token` (triggers a refresh, slow, may pop browser);
   (c) parse the JSON cache on macOS/Linux only. **Leaning (a) for v0.1**, revisit in v0.2.
4. **Subshell prompt injection** per shell (pwsh `function prompt`, bash `PROMPT_COMMAND`/`PS1`,
   zsh `precmd`, fish `fish_prompt`) without stomping the user's existing prompt.
5. **Shell detection** reliability from the parent process on Windows (pwsh vs powershell vs cmd).
6. **`System.CommandLine` vs alternatives under Native AOT** — build a hello-world AOT CLI with
   subcommands and a Spectre.Console table, confirm it publishes clean on all three OSes.

Each spike gets a short written finding in `docs/spikes/`.

---

## 8. Stack

- **Language / runtime:** C# / .NET 10 (LTS). Native AOT (`<PublishAot>true`) → single
  self-contained native binary, no runtime install.
- **Arg parsing:** `System.CommandLine` (2.0, Microsoft, AOT-focused). Fallback if it fights AOT:
  `Spectre.Console.Cli`.
- **Output / tables:** `Spectre.Console` (with AOT care) — fall back to hand-rolled table
  writing if needed.
- **JSON:** `System.Text.Json` source generators (AOT-safe).
- **Process spawning:** `System.Diagnostics.Process`.
- **Tests:** xUnit + a **fake `az`** — a stub executable/script placed on `PATH` that emits canned
  JSON and records the args + `AZURE_CONFIG_DIR` it was called with. Integration tests point
  `AZPM_HOME` at a temp directory.
- **CI:** GitHub Actions matrix (`windows-latest`, `macos-latest`, `ubuntu-latest`) →
  `dotnet publish -r <rid> -c Release` → attach binaries to GitHub Releases.
- **Distribution later:** winget + scoop (Windows), Homebrew tap (macOS/Linux).

---

## 9. Non-goals

- Not a replacement for `az` — it wraps it.
- Not a credential vault in v0.1 — MSAL owns the tokens; we own the directories.
- Not a general Azure management TUI.
- No cloud service, no account, no sync.
