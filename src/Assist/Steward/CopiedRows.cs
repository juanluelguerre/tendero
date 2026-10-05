using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Assist.Steward;

/// <summary>
/// The rows a model wrote out again (P12-10). The panel draws every row a read
/// returned as a table, and the prompt asks the model not to copy them; a small
/// model copies them anyway — eighteen products as a numbered list, a Markdown table
/// of the stock — so the answer says everything twice. Code takes the copy out: a
/// list item or a table row that names a row of a drawn table goes, once there are
/// more than a few of them. Naming a few is what the prompt asks for, and stays.
/// </summary>
public static class CopiedRows
{
    /// <summary>How many rows an answer may name before naming them is copying the table.</summary>
    public const int AFew = 3;

    /// <summary>A cell shorter than this names nothing: "es", "en", a one-letter code.</summary>
    private const int ShortestName = 3;

    public static string Without(string text, IEnumerable<ResultTable> tables)
    {
        // What identifies a row: its words — a code, a name, a SKU — never a figure
        // or one of the shop's terms, which a sentence may use for any reason.
        var names = tables
            .SelectMany(table => table.Rows.SelectMany(row => row
                .Where((cell, column) => table.Columns[column].Kind == CellKind.Text && cell.Text is { Length: >= ShortestName })
                .Select(cell => cell.Text!)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
            return text;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var copied = lines.Select(line => IsARow(line) && names.Any(name => line.Contains(name, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (copied.Count(row => row) <= AFew)
            return text;

        var kept = new List<string>();
        int? under = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var indent = line.Length - line.TrimStart().Length;

            // What hangs from an item that went — its details, a line deeper — goes with it.
            if (under is { } parent && line.Trim().Length > 0 && indent > parent)
                continue;
            under = null;

            if (copied[index])
            {
                under = indent;
                WithoutItsLeadIn(kept);
                continue;
            }

            // A table whose rows went leaves its heading and its rule: they go too.
            if (IsTableLine(line) && InACopiedTable(lines, copied, index))
                continue;

            // One blank line between what is left, never the gap a list left behind.
            if (line.Trim().Length == 0 && (kept.Count == 0 || kept[^1].Trim().Length == 0))
                continue;

            kept.Add(line);
        }

        return string.Join('\n', kept).Trim();
    }

    /// <summary>
    /// The line that announced the rows — "Here are a few:" — announces nothing once
    /// they are gone. One that carries a figure is a statement of its own ("18 products
    /// lack it:") and stays, ending as a sentence; one that does not goes with the rows.
    /// </summary>
    private static void WithoutItsLeadIn(List<string> kept)
    {
        var last = kept.FindLastIndex(line => line.Trim().Length > 0);
        if (last < 0 || IsARow(kept[last]))
            return;

        var line = kept[last].TrimEnd();
        var colon = line.TrimEnd('*', '_').Length - 1;
        if (colon < 0 || line[colon] != ':')
            return;

        if (line.Any(char.IsAsciiDigit))
            kept[last] = string.Concat(line.AsSpan(0, colon), ".", line.AsSpan(colon + 1));
        else
            kept.RemoveRange(last, kept.Count - last);
    }

    private static bool IsARow(string line) => IsTableLine(line) || IsAnItem(line.TrimStart());

    private static bool IsTableLine(string line) => line.TrimStart().StartsWith('|');

    /// <summary>"- ", "* ", "+ ", "1. " or "1) ".</summary>
    private static bool IsAnItem(string line)
    {
        if (line.Length > 1 && line[0] is '-' or '*' or '+' && line[1] == ' ')
            return true;

        var digits = line.TakeWhile(char.IsAsciiDigit).Count();
        return digits > 0 && line.Length > digits + 1 && line[digits] is '.' or ')' && line[digits + 1] == ' ';
    }

    /// <summary>Whether the run of table lines this one belongs to has a row that went.</summary>
    private static bool InACopiedTable(string[] lines, bool[] copied, int index)
    {
        var first = index;
        while (first > 0 && IsTableLine(lines[first - 1]))
            first--;
        var last = index;
        while (last < lines.Length - 1 && IsTableLine(lines[last + 1]))
            last++;

        return Enumerable.Range(first, last - first + 1).Any(line => copied[line]);
    }
}
