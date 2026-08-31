# azpm — Work Plan

Companion to [SPEC.md](SPEC.md) and [docs/commands.md](docs/commands.md). Each change lands as a
commit that builds green; work is tracked as GitHub issues.

Current: **`v0.2.2-pre`**, 21 commands, 121 tests, CI green on win/mac/linux.

---

## Shipped

### v0.1 — the core (issues #1–#6, all closed)

- **Spikes (S1–S6):** `AZURE_CONFIG_DIR` isolation proven with real tenants; Native AOT publish
  clean; `System.CommandLine` 2.0 is GA. Findings in `docs/spikes/`.
- **Commands:** `add`, `ls` (`--json`), `path`, `current`, `exec`, `shell`, `use`, `init`,
  `deactivate`, `login`, `logout`, `rm`.
- Handlers DI'd behind `IAzRunner`; `Guard()` turns `AzpmException` into clean stderr + exit
  code; `CommandResolver` wraps `az.cmd`; xUnit v3 with `FakeAzRunner`.
- CI matrix + `release.yml` (tag `v*` → AOT binaries + sha256 → GitHub prerelease).

### v0.2 — backlog (issue #8)

| # | Item | Status |
|---|---|---|
| 1 | **Portal browser integration** — `azpm portal <name> [blade]`, Edge/Chrome `--profile-directory`, Firefox `-P`, tenant-pinned URL + `login_hint`, mapping in `meta.json` | ✅ |
| 2 | **Directory auto-switch** — `azpm local <name>` writes `.azpm`; `azpm init <shell> --auto` follows it on `cd`, `AZPM_AUTO` tracking | ✅ |
| 3 | **Service principals** — `azpm add --sp --client-id --tenant (--client-secret\|--client-secret-stdin\|--certificate)`; `sp.json` plaintext 0600 (keychain → #9); `login` re-auths silently / rotates; `ls` marks `(sp)` | ✅ |
| 4 | **Prompt module** — `azpm prompt [--format]`, `docs/prompt.md` | ✅ |
| 5 | **Terraform/SDK env** — `exec`/`shell`/`use` export `ARM_SUBSCRIPTION_ID` / `ARM_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | ✅ |
| — | `azpm import <name> [--from]`, `azpm rename <old> <new>`, `azpm ls --check` (real token probe) | ✅ |
| 6 | **Azure PowerShell context isolation** | ⏳ [#10](https://github.com/rockymcclamrock/azpm/issues/10) — blocked, no `Az` module to test against |
| 7 | **Keychain-backed SP secrets / tokens** | ⏳ [#9](https://github.com/rockymcclamrock/azpm/issues/9) |

---

## Security review (2026-08-30, issues #11–#18)

| # | Item | Status |
|---|---|---|
| 11 | MCP `--debug`/`--verbose` token leak | ✅ blocked + output scrubbed |
| 12 | MCP "read-only" returned secrets | ✅ secret-surface denylist; `az rest` → GET/HEAD, no body, ARM-only |
| 15 | MCP output unbounded | ✅ 256 KB cap |
| 13a | `.azpm` name unvalidated | ✅ validated in `LocalHandler` |
| 13b | `.azpm` trust model | ✅ `~/.azpm/trust.json` gate + `azpm local --allow` + `init --fullauto` opt-out |
| 14 | `Load` name / `--browser-profile` / temp perms | ✅ all three |
| 16 | `import` `string.Replace` path bug | ✅ `Path.GetRelativePath` + skip reparse points |
| 17 | SP secret on argv + false Windows-ACL claim | ✅ DPAPI-encrypted at rest on Windows; secret passed to `az` via short-lived `-p @file` (not argv); `--certificate` flag for cert auth; false ACL claim removed |
| 18 | SECURITY.md / threat model | ✅ `SECURITY.md` |
| 19 | per-profile MCP opt-out | ✅ `azpm mcp hide/show` |

## Before making the repo public

- [ ] Enable **private vulnerability reporting** (Settings → Advanced Security) — not available
      while private; `SECURITY.md` already points at it.
- [ ] Review open issues for any **unfixed** weakness described with a working repro. (As of the
      #11–#19 sweep none of the security issues are open; re-check before flipping public.)
- [ ] Consider converting future security reports to **GitHub Security Advisories** (private
      draft → published, optional CVE) instead of plain issues.
- [ ] `git log` and closed-issue comments already contain `redacted@example.invalid` (commit author) and the
      full fix narratives — fine post-fix, but be aware they become visible.

## Now / next

- [ ] **Dogfood** on the real dev/prod tenants; log friction to `FEEDBACK.md`.
- [ ] [#9](https://github.com/rockymcclamrock/azpm/issues/9) — SP secrets → OS keychain.
      Windows DPAPI landed as an interim (#17); the remaining work is macOS Keychain / libsecret
      on the POSIX side.
- [ ] [#7](https://github.com/rockymcclamrock/azpm/issues/7) — run `shell` + `init` on a real
      Linux/macOS box (bash/zsh/fish paths are built + unit-tested, never executed). Now also
      covers the trust-gate hook branches (exit-5 nudge).
- [ ] [#1](https://github.com/rockymcclamrock/azpm/issues/1) — S1 reboot re-check:
      `& C:\src\azp\scratch\s1\block6.ps1` after a restart, then delete `scratch/s1`.
- [ ] [#10](https://github.com/rockymcclamrock/azpm/issues/10) — Azure PowerShell isolation
      (needs a machine with `Az.Accounts`; first check if it already honors `AZURE_CONFIG_DIR`).

## Later

- ~~TUI picker~~ ✅ `azpm` (no args) → numbered profile list → opens that profile's shell.
  (arrow-key/fuzzy version could come later)
- ~~MCP server~~ ✅ `azpm mcp` — read-only, hand-rolled JSON-RPC/stdio, no deps.
  `azpm_list_profiles` + `azpm_az` (read-only classifier). `docs/mcp.md`.
  (full-access / opt-in-profiles modes deferred — user chose read-only)
- Firefox containers for `portal`; browser-profile name auto-discovery.
- `--reset` semantics review; shell completion.

---

## Settled decisions

- **Name** `azpm` (`azp` collides with Azure Pipelines `AZP_*` + the old `azp-cli`).
- **Stack** C# / .NET 10, Native AOT, `System.CommandLine` 2.0. License MIT.
  Repo `rockymcclamrock/azpm` (private).
- **`ls` status** — local-file `ready`/`logged out` by default; `--check` does the real probe.
- **Primary switching UX** — `azpm shell` (zero setup) is the lead; `azpm use` + `azpm init`
  (nvm/direnv-style hook) for in-place.
- **SP secret storage** — plaintext `sp.json` 0600 for now; OS keychain is [#9](https://github.com/rockymcclamrock/azpm/issues/9).
