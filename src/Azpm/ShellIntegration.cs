using System.Text;

namespace Azpm;

/// <summary>
/// Emits shell snippets for <c>azpm use</c> / <c>azpm deactivate</c> (env exports) and
/// <c>azpm init</c> (the wrapper function that eval's them into the live shell).
/// </summary>
public static class ShellIntegration
{
    public const string Marker = "AZPM_SHELL_INTEGRATION";

    public static string ShellName(ShellKind kind) => kind switch
    {
        ShellKind.Pwsh => "pwsh",
        ShellKind.PowerShell => "powershell",
        ShellKind.Cmd => "cmd",
        ShellKind.Bash => "bash",
        ShellKind.Zsh => "zsh",
        ShellKind.Fish => "fish",
        _ => kind.ToString().ToLowerInvariant(),
    };

    public static string UseScript(ShellKind kind, AzpmHome home, Profile profile)
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in ProfileEnv.Collect(home, profile))
            sb.AppendLine(Assign(kind, name, value));
        return sb.ToString();
    }

    public static string DeactivateScript(ShellKind kind)
    {
        var sb = new StringBuilder();
        foreach (var name in ProfileEnv.ClearOnDeactivate)
            sb.AppendLine(Clear(kind, name));
        return sb.ToString();
    }

    private static string Assign(ShellKind kind, string name, string value) => kind switch
    {
        ShellKind.Pwsh or ShellKind.PowerShell => $"$env:{name} = {PoshLit(value)}",
        ShellKind.Cmd => $"set \"{name}={value}\"",
        ShellKind.Fish => $"set -gx {name} {ShLit(value)}",
        _ => $"export {name}={ShLit(value)}",
    };

    private static string Clear(ShellKind kind, string name) => kind switch
    {
        ShellKind.Pwsh or ShellKind.PowerShell => $"$env:{name} = $null",
        ShellKind.Cmd => $"set \"{name}=\"",
        ShellKind.Fish => $"set -e {name}",
        _ => $"unset {name}",
    };

    /// <summary>The rc/profile file the user should add the setup line to.</summary>
    public static string ProfileFile(ShellKind kind) => kind switch
    {
        ShellKind.Pwsh or ShellKind.PowerShell => "$PROFILE",
        ShellKind.Bash => "~/.bashrc",
        ShellKind.Zsh => "~/.zshrc",
        ShellKind.Fish => "~/.config/fish/config.fish",
        _ => "your shell profile",
    };

    /// <summary>The one line that wires up <c>azpm use</c> / <c>deactivate</c> for a shell.</summary>
    public static string SetupLine(ShellKind kind, bool auto = false)
    {
        var name = ShellName(kind);
        var flag = auto ? " --auto" : "";
        return kind is ShellKind.Pwsh or ShellKind.PowerShell
            ? $"azpm init {name}{flag} | Out-String | Invoke-Expression"
            : kind is ShellKind.Fish
                ? $"azpm init {name}{flag} | source"
                : $"eval \"$(azpm init {name}{flag})\"";
    }

    public static string InitHeader(ShellKind kind, bool auto) => $"""
        # This is a {ShellName(kind)} snippet — it does nothing on its own.
        # Add this one line to {ProfileFile(kind)}, then open a new shell:
        #     {SetupLine(kind, auto)}
        # (it enables 'azpm use' / 'azpm deactivate'{(auto ? " and .azpm auto-switching" : "")} in that shell.)

        """;

    public static string InitScript(ShellKind kind, string exePath)
    {
        switch (kind)
        {
            case ShellKind.Pwsh or ShellKind.PowerShell:
                return $$"""
                    function azpm {
                        $__head = $args | Select-Object -First 3
                        if (($__head -contains 'use') -or ($__head -contains 'deactivate')) {
                            $__azpm = & {{PoshLit(exePath)}} @args --emit
                            if ($LASTEXITCODE -eq 0 -and $__azpm) { ($__azpm -join "`n") | Invoke-Expression }
                            elseif ($__azpm) { $__azpm }
                        } else {
                            & {{PoshLit(exePath)}} @args
                        }
                    }

                    """;

            case ShellKind.Fish:
                return $$"""
                    function azpm
                        if contains -- use $argv[1..3]; or contains -- deactivate $argv[1..3]
                            {{ShLit(exePath)}} $argv --emit | source
                        else
                            {{ShLit(exePath)}} $argv
                        end
                    end

                    """;

            case ShellKind.Bash or ShellKind.Zsh:
                return $$"""
                    azpm() {
                        case " $1 $2 $3 " in
                            *" use "*|*" deactivate "*)
                                eval "$(command {{ShLit(exePath)}} "$@" --emit)" ;;
                            *)
                                command {{ShLit(exePath)}} "$@" ;;
                        esac
                    }

                    """;

            default:
                throw new AzpmException(ExitCode.UsageError,
                    $"'azpm init' doesn't support {ShellName(kind)}; use 'azpm shell' instead");
        }
    }

    /// <summary>
    /// The <c>azpm init --auto</c> directory hook: on each directory change, reconcile the shell
    /// to the nearest <c>.azpm</c> file (like nvm/direnv). Auto-set profiles are tracked in
    /// <c>AZPM_AUTO</c> so a manual <c>azpm use</c> isn't clobbered.
    /// </summary>
    public static string AutoHookScript(ShellKind kind, string exePath) => kind switch
    {
        ShellKind.Pwsh or ShellKind.PowerShell => $$"""
            $global:__azpm_pwd = $null
            $global:__azpm_prompt_base = $function:prompt
            function prompt {
                if ($PWD.Path -ne $global:__azpm_pwd) {
                    $global:__azpm_pwd = $PWD.Path
                    $want = & {{PoshLit(exePath)}} local --resolve 2>$null
                    if ($LASTEXITCODE -ne 0) { $want = '' }
                    if ($want -ne $env:AZPM_PROFILE) {
                        if ($want) { azpm use $want | Out-Null; $env:AZPM_AUTO = $want }
                        elseif ($env:AZPM_PROFILE -and $env:AZPM_AUTO -eq $env:AZPM_PROFILE) {
                            azpm deactivate | Out-Null; $env:AZPM_AUTO = $null
                        }
                    }
                }
                & $global:__azpm_prompt_base
            }

            """,
        ShellKind.Fish => $$"""
            function __azpm_auto --on-variable PWD
                set -l want ({{ShLit(exePath)}} local --resolve 2>/dev/null)
                if test "$want" != "$AZPM_PROFILE"
                    if test -n "$want"
                        azpm use $want >/dev/null; set -gx AZPM_AUTO $want
                    else if test -n "$AZPM_PROFILE" -a "$AZPM_AUTO" = "$AZPM_PROFILE"
                        azpm deactivate >/dev/null; set -e AZPM_AUTO
                    end
                end
            end
            __azpm_auto

            """,
        _ => $$"""
            __azpm_auto() {
                [ "$PWD" = "$__azpm_pwd" ] && return
                __azpm_pwd="$PWD"
                local want
                want="$(command {{ShLit(exePath)}} local --resolve 2>/dev/null)" || want=""
                if [ "$want" != "$AZPM_PROFILE" ]; then
                    if [ -n "$want" ]; then
                        eval "$(command {{ShLit(exePath)}} use "$want" --emit)"; export AZPM_AUTO="$want"
                    elif [ -n "$AZPM_PROFILE" ] && [ "$AZPM_AUTO" = "$AZPM_PROFILE" ]; then
                        eval "$(command {{ShLit(exePath)}} deactivate --emit)"; unset AZPM_AUTO
                    fi
                fi
            }
            case "${PROMPT_COMMAND:-}" in
                *__azpm_auto*) ;;
                *) PROMPT_COMMAND="__azpm_auto${PROMPT_COMMAND:+; $PROMPT_COMMAND}" ;;
            esac
            __azpm_auto

            """,
    };

    /// <summary>PowerShell single-quoted literal.</summary>
    private static string PoshLit(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>POSIX single-quoted literal.</summary>
    private static string ShLit(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
