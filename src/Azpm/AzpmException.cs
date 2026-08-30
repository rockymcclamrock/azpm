namespace Azpm;

/// <summary>
/// A user-facing error. The message is printed to stderr (no stack trace) and
/// <see cref="ExitCode"/> becomes the process exit code.
/// </summary>
public sealed class AzpmException(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
