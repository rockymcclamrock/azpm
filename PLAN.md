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

## Phase 3 — `use` + `init` + `current`

- [ ] `azpm current` — print `AZPM_PROFILE` or exit non-zero.
- [ ] `azpm use <name>` — emit env-export lines for the detected shell; detect the `azpm init`
      wrapper (env marker) and print a setup hint when unwrapped.
- [ ] `azpm init pwsh` — emit the PowerShell function wrapper (dot-source in `$PROFILE`).
      Then `bash` / `zsh` / `fish`.
- [ ] `azpm deactivate` (via the init wrapper) — restore the pre-`use` env.
- [ ] Tests: golden-file the `init` output per shell; round-trip `use` → `deactivate` in a real
      pwsh/bash child.

**Demo:** `azpm init pwsh >> $PROFILE`; new shell; `azpm use prod`; prompt shows `prod`;
`azpm deactivate`.

---

## Phase 4 — Lifecycle polish

- [ ] `azpm login <name>` — re-auth an existing profile.
- [ ] `azpm logout <name>` — `az logout` + clear token cache, keep the profile.
- [ ] `azpm rm <name> [--yes]` — confirm prompt, delete dir.
- [ ] Consistent error surface + exit codes (§6) across every command.
- [ ] `azpm ls` status reflects logged-out profiles (from Phase 0 S3 decision).

**Demo:** full lifecycle — `add` → `use` → `logout` → `ls` (shows logged out) → `login` → `rm`.

---

## Phase 5 — Release `v0.1.0-pre`

- [ ] `README.md`: problem, install, the 3 core workflows (`shell`, `exec`, `init`+`use`),
      limitations (no Portal, no PowerShell-Az, no SPs yet).
- [ ] `docs/` — one page per command; the spike findings.
- [ ] `--version` / `--help` polish; shell completion if cheap.
- [ ] CI release job: AOT publish per RID, checksums, attach to a GitHub pre-release.
- [ ] Tag `v0.1.0-pre`. Dogfood on the real dev/prod tenants for a week; keep a `FEEDBACK.md`.

---

## After v0.1 — prioritized backlog

1. **Portal browser integration** (#2 pain) — launch a URL in a named browser profile / Firefox
   container / Edge workspace tied to the azpm profile.
2. **Directory auto-switch** — `.azpm` file + the `init` hook reacts on `cd`.
3. **Service principals** — `azpm add --service-principal`, cert auth, CI-friendly.
4. **Prompt module** — starship / oh-my-posh segment; richer `shell` prompt.
5. **Terraform/SDK export** — `ARM_*` + `AZURE_*` in `exec`/`shell`/`use`.
6. **Azure PowerShell isolation** — redirect `AzureRmContext`.
7. **Keychain-backed tokens** — materialize on use.
8. **TUI picker**, **MCP server**, **import from `~/.azure`**.

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
