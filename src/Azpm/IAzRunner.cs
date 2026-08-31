namespace Azpm;

public readonly record struct AzResult(int ExitCode, string StdOut, bool TimedOut);

/// <summary>Runs the Azure CLI with <c>AZURE_CONFIG_DIR</c> pointed at a profile.</summary>
public interface IAzRunner
{
    /// <summary>
    /// Runs <c>az</c> with the given args and <c>AZURE_CONFIG_DIR</c> set to <paramref name="configDir"/>.
    /// stdio is inherited (the child talks straight to the terminal). Returns az's exit code.
    /// </summary>
    int Run(string configDir, IReadOnlyList<string> args);

    /// <summary>
    /// Like <see cref="Run"/> but captures stdout, closes stdin (so a prompt fails fast rather
    /// than hanging), and kills the process after <paramref name="timeout"/>.
    /// </summary>
    AzResult Capture(string configDir, IReadOnlyList<string> args, TimeSpan timeout);
}
