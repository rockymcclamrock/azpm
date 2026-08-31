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

## Creating a new browser profile

Point `--browser-profile` at a name that doesn't exist and `azpm` pre-seeds the profile
directory (with a `Preferences` file carrying the display name) and launches Chromium with
`--profile-directory=<name>`; Chromium then registers it and opens a window there. Verified
against Edge + Chrome with the browser already running.

**Managed browsers.** On a work device the browser is managed by group policy and the
new-profile flow breaks in several ways: profile creation may be disabled
(`BrowserAddProfileEnabled=0` Edge / `BrowserAddPersonEnabled=0` Chrome — `azpm portal` checks
these and warns); or the profile is created but device SSO signs it into the primary work
account; or Edge's account-based profile routing reopens the URL in whichever profile already
owns that tenant (symptom: new window loads, times out, portal appears in the regular profile).
None of this is overridable from a launcher.

**Recommendation: use Brave.** It's Chromium (same `--profile-directory`, same auto-create, same
`Local State` format) but is essentially never corporate-managed. `azpm portal <name> --browser
brave --browser-profile <name>` sidesteps all of it. Fallbacks: create the profile in the
managed browser's UI and sign in once; bind to an existing profile and rely on `#@<tenant>` for
the directory switch; `--browser default`.

## Not doing yet

- Auto-discovering browser-profile names (read Chrome/Edge `Local State`) — user names it for now
- Firefox containers
- Storage Explorer / VS Code deep links
