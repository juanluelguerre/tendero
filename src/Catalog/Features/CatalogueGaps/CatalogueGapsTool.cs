using System.ComponentModel;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.CatalogueGaps;

public sealed record CatalogueGapsArguments(
    [property: Description("MissingName, MissingImage, MissingImageAlt or DraftWaiting.")] CatalogueGap Kind,
    [property: Description("For MissingName and MissingImageAlt: the language that is missing, es or en.")] string? Culture = null,
    [property: Description("The language to name the products in: es or en.")] string? NamesIn = null);

public sealed record Gaps(string Kind, string? Culture, int Total, IReadOnlyList<GapItem> Items);

/// <param name="ProductId">What a translation proposal needs.</param>
public sealed record GapItem(string ProductId, string Code, string Name);

/// <summary><c>catalogue_gaps</c> (phase 12, S-3b): what the catalogue lacks, fifty at most with the total.</summary>
public sealed class CatalogueGapsTool(IQueryDispatcher queries) : ReadTool<CatalogueGapsArguments, Gaps>(queries)
{
    public override string Name => "catalogue_gaps";

    public override string Description =>
        "What the catalogue lacks: products with no name in a language, with no picture, with a picture not described in a language, or drafts waiting for review. Returns each product's id, code and name, fifty at most, with the total.";

    protected override async Task<(Gaps Data, int Rows, int? Total)> ReadAsync(
        CatalogueGapsArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var gaps = await queries.SendAsync(new CatalogueGapsQuery(
            arguments.Kind, arguments.Culture, ReadLimits.MaxRows, arguments.NamesIn ?? CultureNegotiation.Default), cancellationToken);

        var data = new Gaps(gaps.Kind.ToString(), gaps.Culture, gaps.Total,
            [.. gaps.Rows.Select(row => new GapItem(row.ProductId, row.Code, row.Name))]);
        return (data, data.Items.Count, gaps.Total);
    }

    protected override IReadOnlyList<ResultTable> Tables(Gaps data) =>
    [
        ResultTable.Of("catalogueGaps",
            [new("code", CellKind.Text), new("name", CellKind.Text)],
            data.Items.Select(row => (IReadOnlyList<object?>)[row.Code, row.Name])),
    ];
}
