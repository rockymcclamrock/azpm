namespace Azpm;

/// <summary>Runs the Azure CLI with <c>AZURE_CONFIG_DIR</c> pointed at a profile.</summary>
public interface IAzRunner
{
    /// <summary>
    /// Runs <c>az</c> with the given args and <c>AZURE_CONFIG_DIR</c> set to <paramref name="configDir"/>.
    /// stdio is inherited (the child talks straight to the terminal). Returns az's exit code.
    /// </summary>
    int Run(string configDir, IReadOnlyList<string> args);
}
