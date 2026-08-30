# Showing the active profile in your prompt

`azpm prompt` prints the active profile (`AZPM_PROFILE`) and **nothing** when none is active,
always exiting 0. It only reads an environment variable — safe to call on every prompt.

```
azpm prompt                       # -> prod
azpm prompt --format ' [az:{}]'   # -> " [az:prod]"   (empty when no profile)
```

## starship (`~/.config/starship.toml`)

```toml
[custom.azpm]
command = "azpm prompt"
when = "azpm prompt"          # only show when a profile is active
symbol = "☁️ "
style = "bold blue"
format = "[$symbol$output]($style) "
```

## oh-my-posh (segment)

```json
{
  "type": "command",
  "style": "plain",
  "properties": { "shell": "bash", "command": "azpm prompt --format 'az:{}'" }
}
```

## PowerShell (`$PROFILE`, no framework)

```powershell
$__base = $function:prompt
function prompt {
    $p = azpm prompt --format '[az:{}] '
    if ($p) { Write-Host $p -NoNewline -ForegroundColor Blue }
    & $__base
}
```

## bash / zsh (`PS1`)

```bash
__azpm_ps1() { azpm prompt --format '[az:%s] ' | sed 's/%s/&/'; }   # or just: azpm prompt --format '[az:{}] '
PS1='$(azpm prompt --format "[az:{}] ")'"$PS1"
```
