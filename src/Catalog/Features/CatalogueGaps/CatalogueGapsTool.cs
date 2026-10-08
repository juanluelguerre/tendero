using System.ComponentModel;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.CatalogueGaps;

public sealed record CatalogueGapsArguments(
    [property: Description("MissingName, MissingImage, MissingImageAlt, DraftWaiting or MissingRequiredAttribute.")] CatalogueGap Kind,
    [property: Description("For MissingName and MissingImageAlt: the language that is missing, es or en.")] string? Culture = null,
    [property: Description("The language to name the products in: es or en.")] string? NamesIn = null);

public sealed record Gaps(string Kind, string? Culture, int Total, IReadOnlyList<GapItem> Items);

/// <param name="ProductId">What a translation proposal needs.</param>
/// <param name="Lacks">For MissingRequiredAttribute: the attributes it lacks, by their labels. Null for the other kinds.</param>
public sealed record GapItem(string ProductId, string Code, string Name, IReadOnlyList<string>? Lacks = null);

/// <summary><c>catalogue_gaps</c> (phase 12, S-3b): what the catalogue lacks, fifty at most with the total.</summary>
public sealed class CatalogueGapsTool(IQueryDispatcher queries) : ReadTool<CatalogueGapsArguments, Gaps>(queries)
{
    public override ToolAudience Audience => ToolAudience.Shopkeeper;

    public override string Name => "catalogue_gaps";

    public override string Description =>
        "What the catalogue lacks: products with no name in a language, with no picture, with a picture not described in a language, drafts waiting for review, or products missing an attribute their category requires. Returns each product's id, code and name, fifty at most, with the total.";

    protected override async Task<(Gaps Data, int Rows, int? Total)> ReadAsync(
        CatalogueGapsArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var gaps = await queries.SendAsync(new CatalogueGapsQuery(
            arguments.Kind, arguments.Culture, ReadLimits.MaxRows, arguments.NamesIn ?? CultureNegotiation.Default), cancellationToken);

        var data = new Gaps(gaps.Kind.ToString(), gaps.Culture, gaps.Total,
            [.. gaps.Rows.Select(row => new GapItem(
                row.ProductId, row.Code, row.Name, gaps.Kind == CatalogueGap.MissingRequiredAttribute ? row.Lacks : null))]);
        return (data, data.Items.Count, gaps.Total);
    }

    /// <summary>The same table for every kind; the one kind that is about several things at once says which, in a third column.</summary>
    protected override IReadOnlyList<ResultTable> Tables(Gaps data) =>
        data.Kind == nameof(CatalogueGap.MissingRequiredAttribute)
            ?
            [
                ResultTable.Of("catalogueGaps",
                    [new("code", CellKind.Text), new("name", CellKind.Text), new("lacks", CellKind.Text)],
                    data.Items.Select(row => (IReadOnlyList<object?>)[row.Code, row.Name, string.Join(", ", row.Lacks ?? [])])),
            ]
            :
            [
                ResultTable.Of("catalogueGaps",
                    [new("code", CellKind.Text), new("name", CellKind.Text)],
                    data.Items.Select(row => (IReadOnlyList<object?>)[row.Code, row.Name])),
            ];
}
