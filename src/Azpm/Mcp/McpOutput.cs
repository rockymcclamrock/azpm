using System.Text.RegularExpressions;

namespace Azpm.Mcp;

/// <summary>Post-processing for text returned by <c>azpm_az</c>: redact stray bearer tokens
/// (defence in depth behind the <c>--debug</c> block) and cap the size fed into the model.</summary>
public static partial class McpOutput
{
    public const int MaxBytes = 256 * 1024;

    [GeneratedRegex(@"(?i)(bearer\s+)[A-Za-z0-9._\-]{20,}")]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"(?i)(""access[_-]?token""\s*:\s*"")[^""]{20,}(""?)")]
    private static partial Regex AccessTokenField();

    public static string Sanitize(string text)
    {
        text = BearerToken().Replace(text, "$1<redacted>");
        text = AccessTokenField().Replace(text, "$1<redacted>$2");
        return Cap(text);
    }

    private static string Cap(string text)
    {
        var total = System.Text.Encoding.UTF8.GetByteCount(text);
        if (total <= MaxBytes)
            return text;

        // Keep whole chars whose UTF-8 encoding fits in the budget.
        var budget = MaxBytes - 96;
        int kept = 0, used = 0;
        while (kept < text.Length)
        {
            var w = char.IsHighSurrogate(text[kept]) && kept + 1 < text.Length ? 2 : 1;
            var b = System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(kept, w));
            if (used + b > budget)
                break;
            used += b;
            kept += w;
        }

        return string.Concat(
            text.AsSpan(0, kept),
            $"\n… [output truncated at {MaxBytes / 1024} KB, {total - kept} more bytes; narrow it with --query or -o tsv]");
    }
}
