# S1 — `AZURE_CONFIG_DIR` isolation proof

**Question:** Does pointing `AZURE_CONFIG_DIR` at a per-profile directory give two Azure CLI
logins (different tenants, different accounts) that are fully independent — no cross-contamination,
instant switching, survive a new shell and a reboot?

**Date:** 2026-08-29
**Machine:** Windows 10, PowerShell 7 + Windows PowerShell 5.1, Azure CLI 2.75.0

> Real tenant names / account UPNs / subscription IDs from the test run have been replaced with
> placeholders (`dev.example.com`, `prod.example.com`, `you@…`). The raw run stayed local.

## Baseline (real `~/.azure`, must stay untouched)

- Logged in as a personal account (`you@personal.example.com`), unrelated tenant
- Token cache on Windows: `msal_token_cache.bin` + `msal_http_cache.bin` (DPAPI-encrypted, ~21 KB)
- `azureProfile.json` per-subscription fields: `id`, `name`, `state`, `user.{name,type}`,
  `isDefault`, `tenantId`, `homeTenantId`, `tenantDefaultDomain`, `tenantDisplayName`
- `~/.azure/azureProfile.json` mtime recorded, to detect any write

## Test steps

| # | Check | Expected | Result |
|---|---|---|---|
| 1 | `az login` with `AZURE_CONFIG_DIR=scratch\dev` writes only under that dir | new dir populated, `~/.azure` mtime unchanged | ✅ dir self-populated; `~/.azure` mtime stayed at baseline |
| 2 | Fresh shell, no env var → `az account show` | still the baseline account | ✅ unchanged |
| 3 | Login into `scratch\prod` (other tenant + account) | independent profile | ✅ `you@prod.example.com / prod.example.com / prod-sub` |
| 4 | Flip env var between dev/prod, `az account show` each time | correct tenant+user instantly, **no browser** | ✅ instant, no browser |
| 5 | Brand-new shell, set env var, `az account show` | works with no login | ✅ both profiles resolve |
| 6 | `az account get-access-token` in each (new shell) | real token issued, refresh token valid | ✅ distinct tokens, distinct tenants, distinct expiry, no browser |
| 7 | After reboot, repeat 4–6 | still works (DPAPI cache survived) | ⏳ pending user reboot |

_(Note: `az group list --query "length(@)"` failed under Windows PowerShell 5.1 with
`-o was unexpected at this time` — a quoting bug in the `az.cmd` batch wrapper, not an isolation
issue. `azpm` will invoke `az` as a process directly and sidestep it. Carry into S2.)_

## Findings

- **Isolation is total.** With `AZURE_CONFIG_DIR` set, `az login` / `az account *` never read or
  write `~/.azure`. A concurrent shell with no env var is unaffected. Confirmed by mtime + a
  second terminal.
- **Switching = one env var, zero re-login.** `$env:AZURE_CONFIG_DIR = <dir>` then `az` — no
  browser, no delay. This is the whole product.
- **Persistence works.** A brand-new shell pointed at a profile dir issues fresh access tokens
  via `az account get-access-token` with no browser — the MSAL refresh token in the isolated
  cache is honored. (Reboot survival still to confirm, but expected: DPAPI cache is user-scoped.)
- **`az login` is ADDITIVE within a config dir.** Logging in twice with different identities
  merged *all* visible subscriptions from both into one `azureProfile.json` (seen: 10 subs /
  3 accounts / 4 domains), including cross-tenant guest subs.
  → `azpm add` MUST start from a fresh empty dir (it does).
  → `azpm login` (re-auth) needs a guard: warn / require `--reset` if the account differs.
- **The `az login` picker is interactive** ("Type a number or Enter for no changes"). For a clean
  `azpm add` UX, set `az config set core.login_experience_v2=off` (scoped to the profile dir)
  before invoking `az login`, or pass `--tenant`. (Carry into S2.)
- **Profile dir is fully self-contained:** `azureProfile.json`, `msal_token_cache.bin`,
  `msal_http_cache.bin`, `config`, `commands/`, `logs/`, `telemetry/`, plus assorted small json.
- **`azureProfile.json` fields for `azpm ls`** (confirmed): per subscription — `id`, `name`,
  `state`, `user.name`, `user.type`, `isDefault`, `tenantId`, `homeTenantId`,
  `tenantDefaultDomain`, `tenantDisplayName`. `isDefault:true` = active subscription.
- **Windows invocation caveat:** shelling to `az.cmd` with args containing `()` / `@` breaks
  under Windows PowerShell. `azpm` must spawn the real `az` entrypoint
  (`…\CLI2\python.exe -IBm azure.cli` on Windows, or `az` on PATH elsewhere) via `Process` with
  an argument array — never through a shell string.

## Decision

**PASS.** The core mechanism is sound — build on it. Only the reboot-persistence check remains,
and it is not expected to change the conclusion.
