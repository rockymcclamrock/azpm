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

    public static string UseScript(ShellKind kind, string profile, string configDir, string home) => kind switch
    {
        ShellKind.Pwsh or ShellKind.PowerShell => $"""
            $env:AZURE_CONFIG_DIR = {PoshLit(configDir)}
            $env:AZPM_PROFILE = {PoshLit(profile)}
            $env:AZPM_HOME = {PoshLit(home)}

            """,
        ShellKind.Cmd => $"""
            set "AZURE_CONFIG_DIR={configDir}"
            set "AZPM_PROFILE={profile}"
            set "AZPM_HOME={home}"

            """,
        ShellKind.Fish => $"""
            set -gx AZURE_CONFIG_DIR {ShLit(configDir)}
            set -gx AZPM_PROFILE {ShLit(profile)}
            set -gx AZPM_HOME {ShLit(home)}

            """,
        _ => $"""
            export AZURE_CONFIG_DIR={ShLit(configDir)}
            export AZPM_PROFILE={ShLit(profile)}
            export AZPM_HOME={ShLit(home)}

            """,
    };

    public static string DeactivateScript(ShellKind kind) => kind switch
    {
        ShellKind.Pwsh or ShellKind.PowerShell => """
            $env:AZURE_CONFIG_DIR = $null
            $env:AZPM_PROFILE = $null

            """,
        ShellKind.Cmd => """
            set "AZURE_CONFIG_DIR="
            set "AZPM_PROFILE="

            """,
        ShellKind.Fish => """
            set -e AZURE_CONFIG_DIR
            set -e AZPM_PROFILE

            """,
        _ => """
            unset AZURE_CONFIG_DIR AZPM_PROFILE

            """,
    };

    public static string InitScript(ShellKind kind, string exePath)
    {
        switch (kind)
        {
            case ShellKind.Pwsh or ShellKind.PowerShell:
                return $$"""
                    function azpm {
                        if ($args.Count -ge 1 -and ($args[0] -eq 'use' -or $args[0] -eq 'deactivate')) {
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
                        if test "$argv[1]" = use -o "$argv[1]" = deactivate
                            {{ShLit(exePath)}} $argv --emit | source
                        else
                            {{ShLit(exePath)}} $argv
                        end
                    end

                    """;

            case ShellKind.Bash or ShellKind.Zsh:
                return $$"""
                    azpm() {
                        if [ "$1" = "use" ] || [ "$1" = "deactivate" ]; then
                            eval "$(command {{ShLit(exePath)}} "$@" --emit)"
                        else
                            command {{ShLit(exePath)}} "$@"
                        fi
                    }

                    """;

            default:
                throw new AzpmException(ExitCode.UsageError,
                    $"'azpm init' doesn't support {ShellName(kind)}; use 'azpm shell' instead");
        }
    }

    /// <summary>PowerShell single-quoted literal.</summary>
    private static string PoshLit(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>POSIX single-quoted literal.</summary>
    private static string ShLit(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
