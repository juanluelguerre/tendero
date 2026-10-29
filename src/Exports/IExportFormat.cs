using System.Globalization;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Exports;

/// <summary>
/// The outbound port (E-1; spec, *An outbound port, mirrored on the inbound ones*):
/// how one kind of row is written in one format. Each <b>what</b> is a slice that
/// gathers its rows — a feed's items, a catalogue's products, an accountant's order
/// lines — and each <b>how</b> is one of these, registered keyed by its
/// <see cref="Format"/> for its kind of row, as a catalogue source is keyed by its
/// name. A new format is a class and a registration line; a slice asks
/// <see cref="IExportFormats"/> and holds no <c>if</c> on the format.
/// </summary>
/// <typeparam name="TRow">The rows it writes: one slice's own record.</typeparam>
public interface IExportFormat<TRow>
{
    /// <summary>A stable lowercase identifier: "google-merchant", "shopify-csv", "csv"…</summary>
    string Format { get; }

    /// <summary>The file's media type, with its charset.</summary>
    string ContentType { get; }

    /// <summary>The file's extension, without its dot.</summary>
    string Extension { get; }

    Task WriteAsync(IReadOnlyList<TRow> rows, ExportWriting writing, Stream output, CancellationToken cancellationToken = default);
}

/// <param name="Culture">The reader's language, as every answer picks it: what a spreadsheet is split and its numbers written by.</param>
public sealed record ExportWriting(string Culture);

/// <summary>The formats registered, by kind of row and name; the only place that talks to the container.</summary>
public interface IExportFormats
{
    /// <summary>The format of that name for these rows, or null when none is registered.</summary>
    IExportFormat<TRow>? Find<TRow>(string format);
}

internal sealed class KeyedExportFormats(IServiceProvider services) : IExportFormats
{
    public IExportFormat<TRow>? Find<TRow>(string format) => services.GetKeyedService<IExportFormat<TRow>>(format);
}

/// <summary>
/// Money as a number a file's reader can sum (spec, *Money as numbers, not text*):
/// the currency's own decimals — two for the euro, none for the yen — with the
/// reader's decimal mark and never a thousands separator, which a spreadsheet would
/// take for text. Built by hand rather than by a culture's format: the API runs with
/// invariant globalization, where every culture writes a point.
/// </summary>
public static class ExportNumbers
{
    /// <exception cref="ArgumentException">The amount is not a whole number of the currency's minor units: it is rounded where it is computed (ADR 0059), never here.</exception>
    public static string Amount(Money money, char decimalMark = '.')
    {
        var decimals = MinorUnits.Of(money.Currency);
        if (decimal.Round(money.Amount, decimals) != money.Amount)
            throw new ArgumentException($"{money.Amount} {money.Currency} is not a whole number of its minor units.", nameof(money));

        var text = money.Amount.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return decimalMark == '.' ? text : text.Replace('.', decimalMark);
    }
}
