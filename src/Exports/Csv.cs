using System.Text;

namespace ElGuerre.Tendero.Exports;

/// <summary>
/// How a file of cells is written for whoever reads it (E-1; spec, *CSV is written
/// for the spreadsheet a person opens it in*). A spreadsheet gets RFC 4180 — a cell
/// that holds the separator, a quote or a line break is quoted — and a byte order
/// mark, without which Excel reads UTF-8 in its own code page and shows <c>Ã¡</c>
/// for <c>á</c>. A feed follows its consumer's rules instead: Merchant Center reads
/// tab-separated UTF-8 and takes a quote for text, so nothing is quoted and a tab or
/// a line break in a cell becomes a space.
/// </summary>
/// <param name="Separator">What splits one cell from the next.</param>
/// <param name="Quoted">Whether a cell holding the separator, a quote or a line break is quoted, or cleaned of them.</param>
/// <param name="ByteOrderMark">Whether the file says it is UTF-8.</param>
public sealed record CsvDialect(char Separator, bool Quoted, bool ByteOrderMark)
{
    /// <summary>
    /// For a person's spreadsheet, split as their language splits it (decision 4): a
    /// Spanish Excel splits on <c>;</c>, because its decimal separator is the comma,
    /// and an English one on <c>,</c>.
    /// </summary>
    public static CsvDialect ForSpreadsheet(string culture) =>
        new(culture == "es" ? ';' : ',', Quoted: true, ByteOrderMark: true);

    /// <summary>A product feed, as Merchant Center reads one: tabs, no quoting, no BOM.</summary>
    public static readonly CsvDialect Feed = new('\t', Quoted: false, ByteOrderMark: false);
}

/// <summary>
/// The writer every format uses, and the reader that reads it back: twenty lines,
/// where a library would be a dependency (CLAUDE.md, the dependency budget). Lines
/// end in CRLF, as RFC 4180 says.
/// </summary>
public static class Csv
{
    private const string LineBreak = "\r\n";

    public static async Task WriteAsync(
        Stream output, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> records, CsvDialect dialect, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(dialect.ByteOrderMark), leaveOpen: true);
        await writer.WriteAsync(Line(header, dialect).AsMemory(), cancellationToken);
        foreach (var record in records)
            await writer.WriteAsync(Line(record, dialect).AsMemory(), cancellationToken);
    }

    private static string Line(IReadOnlyList<string> cells, CsvDialect dialect) =>
        string.Join(dialect.Separator, cells.Select(cell => Cell(cell, dialect))) + LineBreak;

    private static string Cell(string cell, CsvDialect dialect)
    {
        if (!dialect.Quoted)
            return string.Join(' ', cell.Split(['\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        return cell.AsSpan().IndexOfAny([dialect.Separator, '"', '\r', '\n']) < 0
            ? cell
            : $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>A file's records, its header first; a BOM is skipped, quoted cells unquoted.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Read(ReadOnlySpan<byte> file, char separator)
    {
        var text = new UTF8Encoding(false).GetString(file).TrimStart('﻿');
        var records = new List<IReadOnlyList<string>>();
        var record = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                    cell.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"')
                    cell.Append(text[++i]);
                else
                    quoted = false;
            }
            // A quote opens a quoted cell only where the cell begins (RFC 4180): inside an unquoted one, as a feed writes it, it is text.
            else if (c == '"' && cell.Length == 0)
                quoted = true;
            else if (c == separator)
            {
                record.Add(cell.ToString());
                cell.Clear();
            }
            else if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                continue;
            else if (c == '\n')
            {
                record.Add(cell.ToString());
                records.Add(record);
                (record, cell) = ([], new StringBuilder());
            }
            else
                cell.Append(c);
        }

        if (cell.Length > 0 || record.Count > 0)
        {
            record.Add(cell.ToString());
            records.Add(record);
        }

        return records;
    }
}
