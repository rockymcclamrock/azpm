# Dogfooding notes

Running notes from using `azpm` on real dev/prod tenants. Raw — triage into issues later.

## 2026-08-30

### Friction found → fixed

- **`azpm init` "doesn't do anything, just prints to console".** Working as designed (it's a
  shell snippet to `eval`, like `starship init`) but not discoverable. Fixed: output now leads
  with a `#` header saying where to put it, and prints a stderr note when written straight to a
  terminal.
- **`azpm use` "doesn't work".** Same root cause — no shell integration loaded, so the printed
  exports go nowhere. Fixed: the not-wired-up message now gives the exact setup line for the
  detected shell *and* points at `azpm shell <name>` as the no-setup path. README rewritten with
  a "two ways to switch" table.
- **`azpm portal` still shows an account picker.** The URL pins the tenant; added a best-effort
  `login_hint` for the profile's account. Real fix is one account per browser profile — now
  called out in the `portal` output and docs.

## 2026-08-31

### Friction found → fixed

- **`azpm portal` on a corporate-managed Edge (work laptop).** New Edge profile is created, but
  the window navigates to the portal, times out, then the portal reopens in the user's regular
  profile. Cause: managed Edge device-SSO signs the new profile into the primary work account,
  and account-based profile routing hijacks the target tenant's URL to the profile that already
  owns it — not overridable from a launcher. Also the old `?login_hint=` query string (ahead of
  the `#` fragment) is non-standard for the portal SPA and a likely cause of the timeout.
  Fixed: **dropped `login_hint`** (URL is now just `https://portal.azure.com/#@<tenant>`, tenant
  by domain not GUID); added `Browsers.ProfileCreationBlocked` policy check + upfront warning;
  documented the managed-vs-unmanaged distinction — use any Chromium browser your org isn't
  deploying (personal Chrome / Brave / Vivaldi). User switched to Brave.

- **`azpm use` / `azpm init` are genuinely confusing to a first-time user** — took several
  rounds to explain the "a program can't change its parent shell" idea. v0.2.4: hints reworded
  (no "shell integration" phrase), cmd.exe path fixed, README rebuilt around "which command do
  I use" + a real `--auto` scenario. Longer-term: maybe `use`/`init` shouldn't surface until
  the user opts in, and `shell` should be even more clearly *the* answer.

### Still open

-

### Bugs

-
