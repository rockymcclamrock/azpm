# azpm — Azure Profile Manager

> **Status: early development** (`v0.2.3-pre`) — see [SPEC.md](SPEC.md), [PLAN.md](PLAN.md),
> full reference in [docs/commands.md](docs/commands.md).

`aws-vault` / `granted`, but for Azure. Named, isolated Azure CLI logins you switch between
instantly — no re-login, no "which tenant am I in", no clobbering your other account.

```
azpm add dev
azpm add prod
azpm shell prod          # a shell that IS prod — run az/terraform freely, type `exit` to leave
azpm exec dev -- az group list -o table
azpm ls
```

---

## What it actually does

The Azure CLI keeps **all** of its login state — subscriptions, the active one, the token
cache — in one folder, named by the `AZURE_CONFIG_DIR` environment variable (default `~/.azure`).
Log into a second tenant and it overwrites the first.

`azpm` keeps **one such folder per named profile** (`~/.azpm/profiles/<name>/`) and points
`AZURE_CONFIG_DIR` at the right one. That's the whole trick. No credential extraction — the
Azure CLI still owns its own tokens.

---

## Which command do I use?

| I want to… | Command | Setup needed |
|---|---|---|
| See my profiles and jump into one | `azpm` (no arguments) | none |
| Run **one** command as a profile | `azpm exec prod -- az group list` | none |
| Work as a profile for a **while** (a session) | `azpm shell prod` → … → `exit` | none |
| Switch **this shell** to a profile, no subshell | `azpm use prod` | one-time `azpm init` (see below) |
| Have my shell **pick the profile from the folder** I'm in | `cd` into the project | one-time `azpm init --auto` + `azpm local` |
| Open the **Azure Portal** as a profile | `azpm portal prod` | one-time `--browser` bind |

**If you're not sure: use `azpm shell`.** It needs nothing and covers 90% of use.

---

## `azpm shell` — the everyday one

```console
PS C:\> azpm shell prod
Entering 'prod' (PowerShell). Type 'exit' to leave.
[azpm:prod] PS C:\> az account show --query name -o tsv
Production Subscription
[azpm:prod] PS C:\> terraform apply        # talks to prod
[azpm:prod] PS C:\> exit
PS C:\>                                    # back to normal, prod forgotten
```

It opens a normal shell that has `AZURE_CONFIG_DIR` set to the `prod` profile. Your prompt shows
`[azpm:prod]` so you know. Nothing to install.

---

## `azpm use` + `azpm init` — switch in place

`azpm use prod` sets the profile in your **current** shell — no subshell, no `exit` to remember,
and you can flip back and forth (`azpm use dev`, `azpm use prod`, …) in the same terminal.

But there's a catch, and it's a hard rule of how shells work: **a program cannot change the
environment of the shell that launched it.** When `azpm.exe` sets a variable, that variable dies
when `azpm.exe` exits. So `azpm use` on its own can only *print* the commands that would set it —
something inside your shell has to run them.

`azpm init` gives you that "something". It **prints a small shell function** named `azpm`. You
load it once (in your shell profile), and from then on typing `azpm use prod` goes through that
function, which runs `azpm.exe` and applies its output to your live shell. Every other `azpm`
command is passed straight through, unchanged.

This is the exact same mechanism as `nvm`, `direnv`, `pyenv`, `starship init`, `conda`.

### Setup (once)

| shell | add to | line |
|---|---|---|
| PowerShell | `$PROFILE` | `azpm init powershell \| Out-String \| Invoke-Expression` |
| bash | `~/.bashrc` | `eval "$(azpm init bash)"` |
| zsh | `~/.zshrc` | `eval "$(azpm init zsh)"` |
| fish | `~/.config/fish/config.fish` | `azpm init fish \| source` |

Restart your shell, then:

```console
PS C:\> azpm use prod
PS C:\> azpm current
prod
PS C:\> az account show --query name -o tsv     # prod, in this same shell
Production Subscription
PS C:\> azpm use dev                             # flip, no subshell
PS C:\> azpm deactivate                          # back to your default ~/.azure login
```

**Don't use `azpm use`? Then skip `azpm init` — it does nothing else for you.**

---

## `--auto` — pick the profile from the current folder

Add `--auto` to `init` and drop a `.azpm` file in your project directories. Your shell then
switches profile whenever you `cd`.

### Setup (once)

```powershell
# $PROFILE:
azpm init powershell --auto | Out-String | Invoke-Expression
```
```console
PS C:\> cd C:\src\infra-dev  ; azpm local dev      # writes C:\src\infra-dev\.azpm
PS C:\> cd C:\src\infra-prod ; azpm local prod
```

### Real scenario: Terraform for two environments

You keep `infra-dev` and `infra-prod` as separate repos. With `--auto`:

```console
PS C:\src> cd infra-dev
[azpm:dev] PS C:\src\infra-dev>              # switched itself
[azpm:dev] PS C:\src\infra-dev> terraform apply       # dev. correct.

[azpm:dev] PS C:\src\infra-dev> cd ..\infra-prod
[azpm:prod] PS C:\src\infra-prod>            # switched again
[azpm:prod] PS C:\src\infra-prod> terraform plan      # prod. correct.

[azpm:prod] PS C:\src\infra-prod> cd C:\
PS C:\>                                      # left both trees → back to default
```

You never type an `azpm` command, and you **can't** `terraform apply` the wrong environment —
the identity is nailed to the directory.

**Trust:** because a `.azpm` can name any profile you have, `--auto` only follows files you've
approved. `azpm local <name>` approves the one it writes; `azpm local --allow` approves an
existing file (e.g. one from a cloned repo); editing a file re-arms the check. Don't want it?
`azpm init <shell> --fullauto` skips the trust check entirely.

---

## `azpm portal` — the Portal in the right browser, right tenant

```console
PS C:\> azpm portal --browsers                                       # see your browser profiles
PS C:\> azpm portal prod --browser brave --browser-profile g5-prod   # bind once (saved)
PS C:\> azpm portal prod                                             # thereafter
PS C:\> azpm portal prod /resource/subscriptions                     # deep-link
```

Opens `portal.azure.com` pinned to the profile's tenant, in a browser profile bound to the azpm
profile. `--browser`: `edge` / `chrome` / `brave` / `firefox` / `default`. `--browser-profile`
takes the directory (`Profile 4`) **or** the name shown in the browser (`g5-prod`) —
`azpm portal --browsers` lists both.

Best results: **one browser profile per azpm profile, one account each.** Point `--browser-profile`
at a name that doesn't exist yet and Chromium creates a fresh one — sign in once, done. A
leftover account picker means that browser profile has more than one account in it.

---

## Other commands

```console
azpm login prod [--reset]           # re-authenticate an existing profile
azpm logout prod                    # sign out, keep the profile
azpm rm prod                        # delete it  (prompts unless --yes)
azpm rename old new
azpm import mine [--from ~/.azure]   # turn an existing az login into a profile, no re-auth
azpm ls                             # adds a LOGIN column: how stale each sign-in is
azpm ls --check                     # actually probe each profile's token (slower)
azpm path prod                      # print the profile's AZURE_CONFIG_DIR
azpm prompt --format ' [az:{}]'     # active profile, for your shell prompt (docs/prompt.md)
```

**Service principals:** `azpm add ci --sp --client-id <appId> --tenant <id> --client-secret <s>`
(or `--client-secret-stdin`, or `--certificate <pem>`). Stored at `~/.azpm/profiles/ci/sp.json`
— DPAPI-encrypted on Windows, `chmod 600` plaintext on macOS/Linux ([SECURITY.md](SECURITY.md)).
`azpm login ci` re-auths silently.

`exec` / `shell` / `use` also set `ARM_SUBSCRIPTION_ID`, `ARM_TENANT_ID`,
`AZURE_SUBSCRIPTION_ID` from the active subscription, so Terraform and the Azure SDKs pick up
the right one.

**MCP:** `azpm mcp` runs a **read-only** MCP server (stdio) so an agent can inspect your Azure
through your profiles without switching your context or being able to change anything —
[docs/mcp.md](docs/mcp.md).

---

## Install

Prebuilt single-file binaries (no runtime needed) are on each
[release](https://github.com/rockymcclamrock/azpm/releases) — `win-x64`, `linux-x64`,
`osx-arm64`. Download, check the `.sha256`, put `azpm` on your `PATH`.

Or build from source (needs the .NET 10 SDK):

```
dotnet publish src/Azpm -c Release -r win-x64    # or linux-x64 / osx-arm64
```

See [docs/building.md](docs/building.md) — Windows Native AOT also needs `vswhere.exe` on `PATH`.

---

## Not the same as `azctx` / `aztx`

Those switch the active **subscription within one login**. `azpm` manages **multiple independent
logins** — different tenants, different accounts — side by side.

## Scope

Azure CLI (`az`) with interactive user accounts and service principals, plus Portal browser
integration and per-directory profiles. Azure PowerShell context isolation and OS-keychain
secret storage are on the roadmap ([PLAN.md](PLAN.md)).

## Security

Trust boundaries, credential storage, and the MCP server's guarantees are documented in
[SECURITY.md](SECURITY.md), which also explains how to report a vulnerability privately.

## License

MIT — see [LICENSE](LICENSE).
