namespace Azpm;

/// <summary>Process exit codes. See SPEC.md §6.</summary>
public static class ExitCode
{
    public const int Ok = 0;
    public const int UsageError = 1;
    public const int ProfileNotFound = 2;
    public const int AzNotFound = 3;
    public const int AzFailed = 4;
    public const int Interrupted = 130;
}
