# `azpm portal` — open the Azure Portal in the profile's browser context

## Problem

`azpm` fixes the CLI side of multi-tenant Azure. The browser side is still a mess: one Portal
session at a time, constant "switch directory" dances, wrong-tenant clicks. The AWS crowd solves
this with browser-profile / container integration (`granted`'s `assume -c`).

## Approach

Each `azpm` profile *optionally* maps to a **browser + browser-profile**. `azpm portal <name>`
launches that browser profile straight at the Portal, pinned to the profile's tenant. Browser
profiles keep their own cookies, so once you've signed in there it stays signed in — `azpm`
just has to launch the right one.

```
azpm portal prod                       # opens portal.azure.com/#@<prod tenant> in prod's browser profile
azpm portal prod /resource/subscriptions   # deep-link into a blade
azpm portal prod --browser edge --browser-profile "Profile 2"   # set the mapping (persists) and open
```

The mapping lives in `meta.json`:

```json
{ "browser": { "kind": "edge", "profile": "Profile 2" } }
```

## Browser support (v1)

| kind | launch |
|---|---|
| `edge` | `msedge --profile-directory="<profile>" <url>` |
| `chrome` | `chrome --profile-directory="<profile>" <url>` |
| `firefox` | `firefox -P "<profile>" <url>` (separate profiles, not containers) |
| `default` / unset | hand the URL to the OS; print a hint about `--browser` |

Executable resolution: PATH, then well-known install locations per OS.
Firefox *containers* (needs the "Open URL in Container" extension) — deferred.

## Tenant pinning

`https://portal.azure.com/#@<tenantId>` selects the directory. `tenantId` comes from the
profile's active subscription (`azureProfile.json`); fall back to `tenantDefaultDomain`, then no
pin.

## Not doing yet

- Auto-discovering browser-profile names (read Chrome/Edge `Local State`) — user names it for now
- Firefox containers
- Storage Explorer / VS Code deep links
