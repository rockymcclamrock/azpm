# azpm — Work Plan (v0.1)

Companion to [SPEC.md](SPEC.md). Goal: a dogfoodable `v0.1.0-pre` that manages isolated Azure CLI
profiles and can run a command or a shell in one.

Rough size: ~1 focused week if spikes go clean. Each phase ends in a commit that builds and has
green tests. Work is tracked as GitHub issues (one per phase task).

---

## Phase 0 — Spikes & decisions  *(no product code yet)*

Answer the §7 unknowns. Output: short notes in `docs/spikes/`, and locked decisions below.

- [ ] **S1 — Isolation proof.** Two real tenants, two `AZURE_CONFIG_DIR`s, manual. Confirm no
      cross-contamination; both survive a reboot. *This de-risks the whole project.* **(needs the
      user at the keyboard for the MFA logins)**
- [ ] **S2 — `az login` into a fresh dir.** Browser vs `--device-code`; cancel behavior; exit
      codes; stderr shape.
- [ ] **S3 — `ls` status source.** Decide (a)/(b)/(c) from §7.3. Record the `azureProfile.json`
      shape actually seen.
- [ ] **S4 — Prompt injection** snippet per shell (pwsh first).
- [ ] **S5 — Shell detection** from parent process on Windows.
- [ ] **S6 — AOT skeleton.** Hello-world `System.CommandLine` + `Spectre.Console` table,
      `PublishAot`, built on win/mac/linux. Lock the parsing + table libraries here.

**Exit criteria:** S1 passes, S6 publishes clean on all three OSes, library choices locked.

---

## Phase 1 — Skeleton + `add` + `ls`

- [ ] Repo scaffolding: `.gitignore`, `Directory.Build.props`, `global.json` pinned to .NET 10,
      `LICENSE` (MIT), `README.md` stub, solution + `src/Azpm` project with `<PublishAot>true`.
- [ ] `AzpmHome` resolver (`--home` > `AZPM_HOME` > OS default) + `Profile` / `ProfileStore`
      (list, load `meta.json`, resolve `config/` path, name validation).
- [ ] `AzCli` wrapper: locate `az`, run with a given `AZURE_CONFIG_DIR`, capture / inherit
      stdio, surface exit codes.
- [ ] `azpm add <name>` — create dirs, run `az login` (passthrough `--tenant`, `--device-code`),
      write `meta.json`. Handle: profile exists, `az` missing, login cancelled.
- [ ] `azpm ls` — read every profile's `azureProfile.json` + `meta.json`, render the table,
      mark `AZPM_PROFILE`, `--json` variant.
- [ ] Tests: fake `az`; `ProfileStore` unit tests; `add`/`ls` integration tests against a temp
      `AZPM_HOME`.
- [ ] CI workflow: build + test on the 3-OS matrix.

**Demo:** `azpm add dev`, `azpm add prod`, `azpm ls` shows both with account + subscription.

---

## Phase 2 — `exec` + `shell`  ✅ (issue #3)

- [x] `azpm exec <name> -- <cmd...>` — child env (`AZURE_CONFIG_DIR`, `AZPM_PROFILE`,
      `AZPM_HOME`), inherit stdio, propagate exit code. Everything after `--` taken verbatim.
- [x] `azpm path <name>`.
- [x] `azpm current` (pulled forward from Phase 3 — trivial).
- [x] `azpm shell <name>` — shell detection (`--shell` > parent process > `$SHELL` > platform
      default), spawn interactive, prompt prefix that *preserves* the user's prompt, nesting
      guard. pwsh/powershell/cmd verified on Windows; bash/zsh/fish built but unverified.
- [x] `CommandResolver` — PATH+PATHEXT lookup; batch files (`az.cmd`) with spaced paths wrapped
      as `cmd /d /s /c "…"`.
- [x] Tests: 24 new (exec env/exit-code/errors, CommandResolver, Shells). 41 total.

**Demo:** `azpm exec bls-dev -- az account show` / `azpm shell bls-dev` → run `az` freely → `exit`. ✅

Deferred within this area: S4/S5 (prompt injection / shell detection) — done pragmatically here;
bash/zsh/fish prompt polish revisited if dogfooding needs it.

---

## Phase 3 — `use` + `init` + `current`  ✅ (issue #4)

- [x] `azpm current` — print `AZPM_PROFILE` or exit non-zero.
- [x] `azpm use <name>` — emits env-export lines for the detected shell (`--emit` for the
      wrapper); prints a `azpm init <shell>` hint when not wired up.
- [x] `azpm init pwsh|powershell|bash|zsh|fish` — emits the wrapper function. cmd rejected
      (use `azpm shell`).
- [x] `azpm deactivate` — clears the profile env (does not restore a prior `use`).
- [x] Tests: `UseScript` / `InitScript` per shell; hint behavior. Round-trip verified manually
      in pwsh (`use` → `current` → `deactivate`).

**Demo:** `azpm init powershell | iex`; `azpm use g5` → `$env:AZPM_PROFILE` set in place;
`azpm deactivate`. ✅

## Phase 4 — Lifecycle polish  ✅ (issue #5)

- [x] `azpm login <name> [--tenant] [--device-code] [--reset]` — re-auth; `--reset` wipes prior
      az state first; warns if the account changed (S1 additive-login finding).
- [x] `azpm logout <name>` — `az logout`, keeps the profile; no-op if already logged out.
- [x] `azpm rm <name> [--yes]` — `[y/N]` prompt unless `--yes`; `remove` alias.
- [x] `add` now delegates to `LoginHandler`.
- [x] Consistent exit codes via `Guard()`.
- [x] Tests: login/logout/rm with `FakeAzRunner` + scripted stdin. 65 total.

**Demo:** `add` → `use` → `logout` (`ls` shows logged out) → `login` → `rm`. ✅

---

## Phase 4 — Lifecycle polish

- [ ] `azpm login <name>` — re-auth an existing profile.
- [ ] `azpm logout <name>` — `az logout` + clear token cache, keep the profile.
- [ ] `azpm rm <name> [--yes]` — confirm prompt, delete dir.
- [ ] Consistent error surface + exit codes (§6) across every command.
- [ ] `azpm ls` status reflects logged-out profiles (from Phase 0 S3 decision).

**Demo:** full lifecycle — `add` → `use` → `logout` → `ls` (shows logged out) → `login` → `rm`.

---

## Phase 5 — Release `v0.1.0-pre`  ✅ (issue #6)

- [x] `README.md`: problem, install, core workflows, limitations.
- [x] `docs/commands.md` (per-command reference) + `docs/spikes/` + `docs/building.md`.
- [x] `--version` → `0.1.0-pre+<sha>`; `--help` lists every command. (shell completion: deferred)
- [x] `.github/workflows/release.yml` — on a `v*` tag: AOT publish win-x64/linux-x64/osx-arm64,
      tar + sha256, GitHub prerelease.
- [x] Tagged `v0.1.0-pre` →
      [release published](https://github.com/rockymcclamrock/azpm/releases/tag/v0.1.0-pre).
- [ ] Dogfood on the real dev/prod tenants; log to `FEEDBACK.md`.  ← **in progress**

---

## After v0.1 — prioritized backlog

1. ~~**Portal browser integration**~~ ✅ `azpm portal <name>` — Edge/Chrome `--profile-directory`,
   Firefox `-P`, tenant-pinned URL, mapping persisted in `meta.json`. (Firefox *containers* and
   browser-profile auto-discovery still deferred.)
2. ~~**Directory auto-switch**~~ ✅ `azpm local <name>` writes `.azpm`; `azpm init <shell> --auto`
   follows it on `cd` (pwsh/bash/zsh/fish), tracking auto-set profiles in `AZPM_AUTO`.
3. ~~**Service principals**~~ ✅ `azpm add --sp --client-id --tenant (--client-secret |
   --client-secret-stdin | --certificate)`; credential in `sp.json` (plaintext 0600; keychain =
   [#9](https://github.com/rockymcclamrock/azpm/issues/9)). `login` re-auths silently / rotates.
4. ~~**Prompt module**~~ ✅ `azpm prompt [--format]` — env-only, empty+exit-0 when none;
   starship / oh-my-posh / PS1 recipes in `docs/prompt.md`.
5. ~~**Terraform/SDK export**~~ ✅ `ARM_SUBSCRIPTION_ID` / `ARM_TENANT_ID` / `AZURE_SUBSCRIPTION_ID`
   set by `exec`/`shell`/`use` from the active subscription when logged in.
6. **Azure PowerShell isolation** — redirect `AzureRmContext`.
7. **Keychain-backed tokens** — materialize on use.
8. ~~import from `~/.azure`~~ ✅ · ~~`rename`~~ ✅ · ~~`ls` real token status~~ ✅ (`azpm ls --check`).
   Still: **TUI picker** (`azpm` with no args), **MCP server**, Azure PowerShell context
   isolation ([#10](https://github.com/rockymcclamrock/azpm/issues/10)).

---

## Settled decisions

- **Name:** `azpm` (checked: `azp` collides with Azure Pipelines' `AZP_*` agent env vars and the
  old `azp-cli`).
- **Language:** C# / .NET 10, Native AOT. (Go rejected — unfamiliar, not installed.)
- **License:** MIT.
- **Repo:** `rockymcclamrock/azpm`, private until `v0.1.0-pre`.
- **`ls` status (S3):** ship "ready if `azureProfile.json` has an account, else logged out" for
  v0.1; real token-expiry checks deferred to v0.2. *(confirm during S3)*
- **Primary UX:** ship `azpm shell` and `azpm use`/`init` both; lead docs with `shell`.

## Open for the user

- **.NET 10 SDK** — install pending (machine has 9.0.300). Needed before Phase 1.
- **S1 spike** — schedule ~30 min at the keyboard with access to both real tenants.
