using System.Text;

namespace Azpm;

/// <summary>Minimal left-aligned column table (no external dependency, AOT-clean).</summary>
public sealed class TextTable(params string[] headers)
{
    private readonly List<string[]> _rows = [];

    public void AddRow(params string?[] cells) =>
        _rows.Add([.. cells.Select(c => c ?? "")]);

    public void RenderTo(TextWriter writer)
    {
        var widths = new int[headers.Length];
        for (var i = 0; i < headers.Length; i++)
            widths[i] = headers[i].Length;
        foreach (var row in _rows)
            for (var i = 0; i < widths.Length; i++)
                widths[i] = Math.Max(widths[i], row[i].Length);

        writer.WriteLine(Format(headers, widths));
        writer.WriteLine(Format([.. widths.Select(w => new string('-', w))], widths));
        foreach (var row in _rows)
            writer.WriteLine(Format(row, widths));
    }

    private static string Format(string[] cells, int[] widths)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0) sb.Append("  ");
            sb.Append(i == cells.Length - 1 ? cells[i] : cells[i].PadRight(widths[i]));
        }
        return sb.ToString().TrimEnd();
    }
}
