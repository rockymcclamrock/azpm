namespace Azpm;

/// <summary>Process exit codes. See SPEC.md §6.</summary>
public static class ExitCode
{
    public const int Ok = 0;
    public const int UsageError = 1;
    public const int ProfileNotFound = 2;
    public const int AzNotFound = 3;
    public const int AzFailed = 4;

    /// <summary><c>local --resolve</c>: a <c>.azpm</c> is present but not trusted for <c>--auto</c>.</summary>
    public const int NotTrusted = 5;

    public const int Interrupted = 130;
}
