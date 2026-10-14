using System.ComponentModel;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Search.Features.BuildKit;

public sealed record ListKitsArguments(
    [property: Description("The language to answer in: es or en.")] string? Culture = null);

/// <summary>The kits the shop offers, as a sentence about one can be read against them.</summary>
public sealed record KitsOnSale(IReadOnlyList<KitOnSale> Kits);

/// <param name="Requirements">What the kit can be asked to work with: the only requirements a sentence may fill in.</param>
public sealed record KitOnSale(string Code, string Name, IReadOnlyList<KitRequirementOption> Requirements);

/// <summary>
/// <c>list_kits</c> (phase 13c, K-6): the shop's kits, read by the slice in
/// Assist that turns a shopper's sentence into a kit's form. Through the
/// kernel's seam (ADR 0054) because that slice may not reference Search, and
/// for a page, not an agent (<see cref="ToolAudience.Page"/>).
/// </summary>
public sealed class ListKitsTool(IQueryDispatcher queries) : ReadTool<ListKitsArguments, KitsOnSale>(queries)
{
    public override ToolAudience Audience => ToolAudience.Page;

    public override string Name => "list_kits";

    public override string Description =>
        "The kits the shop builds to a budget: each kit's code and name, and what it can be asked to work with.";

    protected override async Task<(KitsOnSale Data, int Rows, int? Total)> ReadAsync(
        ListKitsArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var kits = await queries.SendAsync(new ListKitsQuery(CultureNegotiation.Resolve(arguments.Culture, default)), cancellationToken);
        var onSale = new KitsOnSale([.. kits.Select(kit => new KitOnSale(kit.Code, kit.Name, kit.Requirements))]);
        return (onSale, onSale.Kits.Count, onSale.Kits.Count);
    }
}
