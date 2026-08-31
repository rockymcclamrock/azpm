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

- **`azpm use` / `azpm init` are genuinely confusing to a first-time user** — took several
  rounds to explain the "a program can't change its parent shell" idea. v0.2.4: hints reworded
  (no "shell integration" phrase), cmd.exe path fixed, README rebuilt around "which command do
  I use" + a real `--auto` scenario. Longer-term: maybe `use`/`init` shouldn't surface until
  the user opts in, and `shell` should be even more clearly *the* answer.

### Still open

-

### Bugs

-
