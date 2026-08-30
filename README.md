# azpm — Azure Profile Manager

> **Status: early development.** Building toward `v0.1.0-pre` — see [SPEC.md](SPEC.md) and
> [PLAN.md](PLAN.md). Working today: `add`, `ls`, `path`, `exec`, `shell`, `current`.

`aws-vault` / `granted`, but for Azure. Named, isolated Azure CLI login profiles you switch
between instantly — no re-login, no clobbering your other tenant.

```
azpm add dev            # az login into an isolated profile
azpm add prod
azpm shell prod         # subshell with the prod profile active + prompt marker
azpm exec dev -- az group list      # run one command in a profile
azpm ls                 # see every profile: account, tenant, subscription
azpm path prod          # print the profile's AZURE_CONFIG_DIR
azpm current            # which profile is this shell using
```

Full command reference: [docs/commands.md](docs/commands.md).

## In-place switching

```powershell
azpm init powershell | Out-String | Invoke-Expression   # add to $PROFILE
azpm use prod            # sets AZURE_CONFIG_DIR + AZPM_PROFILE in this shell
azpm deactivate
```

(bash/zsh: `eval "$(azpm init bash)"` · fish: `azpm init fish | source`)

## Install

No published binaries yet. Build from source:

```
dotnet publish src/Azpm -c Release -r win-x64    # or linux-x64 / osx-arm64
# -> src/Azpm/bin/Release/net10.0/<rid>/publish/azpm(.exe) — a single native binary
```

Needs the .NET 10 SDK. See [docs/building.md](docs/building.md) — Windows Native AOT also needs
`vswhere.exe` on `PATH`.

## How it works

The Azure CLI reads all login state (subscriptions, active context, MSAL token cache) from the
directory in `AZURE_CONFIG_DIR`. `azpm` keeps one such directory per named profile and points
`az` at the right one. No credential extraction — MSAL still owns the tokens.

## Not the same as `azctx` / `aztx`

Those switch the active **subscription within a single login**. `azpm` manages **multiple
independent logins** (different tenants, different accounts) side by side.

## Scope

v0.1 covers the Azure CLI (`az`) with interactive user accounts. Azure Portal browser
integration, Azure PowerShell, service principals, and directory auto-switch are on the roadmap
in [PLAN.md](PLAN.md).

## License

MIT — see [LICENSE](LICENSE).
