using System.Text.RegularExpressions;

namespace Azpm;

/// <summary>Profile name rules (SPEC.md §4).</summary>
public static partial class ProfileName
{
    private static readonly string[] Reserved = ["current", "list", "all"];

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string name) =>
        Pattern().IsMatch(name) && !Reserved.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static void Validate(string name)
    {
        if (!IsValid(name))
            throw new AzpmException(ExitCode.UsageError,
                $"invalid profile name '{name}': letters, digits, '.', '_', '-' only (max 64, " +
                "must start alphanumeric), and not a reserved word");
    }
}
