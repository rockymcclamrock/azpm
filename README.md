# azpm — Azure Profile Manager

> **Status: early development** (`v0.2.2-pre`) — see [SPEC.md](SPEC.md), [PLAN.md](PLAN.md),
> [docs/commands.md](docs/commands.md).

`aws-vault` / `granted`, but for Azure. Named, isolated Azure CLI login profiles you switch
between instantly — no re-login, no clobbering your other tenant.

```
azpm add dev                     # az login into an isolated profile
azpm add prod
azpm shell prod                  # <- works right now, no setup: a subshell with prod active
azpm exec dev -- az group list   # run one command in a profile
azpm ls                          # every profile: account, tenant, subscription
azpm portal prod                 # open the Portal in prod's browser profile, right tenant
```

## Two ways to switch

**`azpm shell <name>`** opens a subshell with the profile active. Nothing to set up — use this
first.

**`azpm use <name>`** switches the *current* shell in place — but no program can change its
parent shell's environment, so it needs a one-time hook in your shell profile (exactly like
`nvm`, `direnv`, `starship init`). `azpm init` prints that hook; you add it once:

| shell | add to | line |
|---|---|---|
| PowerShell | `$PROFILE` | `azpm init powershell \| Out-String \| Invoke-Expression` |
| bash | `~/.bashrc` | `eval "$(azpm init bash)"` |
| zsh | `~/.zshrc` | `eval "$(azpm init zsh)"` |
| fish | `~/.config/fish/config.fish` | `azpm init fish \| source` |

Restart the shell, then:

```
azpm use prod         # sets AZURE_CONFIG_DIR / AZPM_PROFILE / ARM_* here
azpm current          # -> prod
azpm deactivate       # back to your default login
```

`azpm init <shell> --auto` additionally follows `.azpm` files as you `cd` (`azpm local prod`
writes one). `azpm prompt` feeds the active profile into your prompt — [docs/prompt.md](docs/prompt.md).

## Install

Prebuilt single-file binaries (no runtime needed) are attached to each
[release](https://github.com/rockymcclamrock/azpm/releases) for `win-x64`, `linux-x64`,
`osx-arm64` — download, verify the `.sha256`, extract `azpm` onto your `PATH`.

Or build from source (needs the .NET 10 SDK):

```
dotnet publish src/Azpm -c Release -r win-x64    # or linux-x64 / osx-arm64
```

See [docs/building.md](docs/building.md) — Windows Native AOT also needs `vswhere.exe` on `PATH`.

## How it works

The Azure CLI reads all login state (subscriptions, active context, MSAL token cache) from the
directory in `AZURE_CONFIG_DIR`. `azpm` keeps one such directory per named profile and points
`az` at the right one. No credential extraction — MSAL still owns the tokens.

## Not the same as `azctx` / `aztx`

Those switch the active **subscription within a single login**. `azpm` manages **multiple
independent logins** (different tenants, different accounts) side by side.

## Scope

Covers the Azure CLI (`az`) with interactive user accounts, plus Portal browser integration and
per-directory profiles. Service principals and Azure PowerShell context isolation are on the
roadmap in [PLAN.md](PLAN.md).

## License

MIT — see [LICENSE](LICENSE).
