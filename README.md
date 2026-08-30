# azpm — Azure Profile Manager

> **Status: pre-implementation.** This repo currently contains only the spec and work plan.
> See [SPEC.md](SPEC.md) and [PLAN.md](PLAN.md).

`aws-vault` / `granted`, but for Azure. Named, isolated Azure CLI login profiles you switch
between instantly — no re-login, no clobbering your other tenant.

```
azpm add dev            # az login into an isolated profile
azpm add prod
azpm shell prod         # subshell with the prod profile active
azpm exec dev -- terraform apply
azpm use prod           # switch the current shell (with `azpm init`)
azpm ls                 # see every profile: account, tenant, subscription
```

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
