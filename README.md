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

Not built yet: `use` / `init` (in-place shell switching), `login` / `logout` / `rm`.
For now, switching in place is just `$env:AZURE_CONFIG_DIR = azpm path <name>`.

## Build

```
dotnet publish src/Azpm -c Release -r win-x64   # -> …/publish/azpm(.exe), a single native binary
```

See [docs/building.md](docs/building.md) (Windows AOT needs `vswhere.exe` on `PATH`).

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
