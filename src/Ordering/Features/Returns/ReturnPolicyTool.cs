using System.ComponentModel;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Ordering.Features.Returns;

public sealed record ReturnPolicyArguments(
    [property: Description("The language to answer in: es or en.")] string? Culture = null);

/// <param name="WindowDays">Days a shopper has to send something back, counted from delivery.</param>
/// <param name="Says">The policy as a sentence, in the language asked for.</param>
public sealed record ReturnPolicyForShopper(int WindowDays, string Says);

/// <summary>
/// <c>get_return_policy</c> (phase 12b): the shop's promise about returns, the one
/// the storefront shows and the MCP server answers — and the only fact here that is
/// about the shop and no product. The sentence is written here, in es and en
/// (invariant 6), so what an answer cites is the shop's wording and its number.
/// </summary>
public sealed class ReturnPolicyTool(IQueryDispatcher queries) : ReadTool<ReturnPolicyArguments, ReturnPolicyForShopper>(queries)
{
    public override ToolAudience Audience => ToolAudience.Shopper;

    public override string Name => "get_return_policy";

    public override string Description =>
        "How many days a shopper has to send something back. The days are counted from delivery, not from the purchase.";

    protected override async Task<(ReturnPolicyForShopper Data, int Rows, int? Total)> ReadAsync(
        ReturnPolicyArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
    {
        var policy = await queries.SendAsync(new ReturnPolicyQuery(), cancellationToken);
        var says = CultureNegotiation.Resolve(arguments.Culture, default) == "en"
            ? $"Returns are accepted for {policy.WindowDays} days from delivery."
            : $"Las devoluciones se aceptan durante {policy.WindowDays} días desde la entrega.";
        return (new ReturnPolicyForShopper(policy.WindowDays, says), 1, null);
    }

    protected override IReadOnlyList<Fact> Facts(ReturnPolicyForShopper data) =>
        [new Fact(Fact.TheShop, FactSource.Policy, data.Says)];
}
