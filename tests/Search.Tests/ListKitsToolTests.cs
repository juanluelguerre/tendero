using System.Text.Json;
using ElGuerre.Tendero.Search.Features.BuildKit;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Search.Tests;

/// <summary>
/// What the sentence that fills a kit's form may read of the shop's kits (phase
/// 13c, K-6): which kits there are, by code and name, and what each can be
/// asked to work with. Read through the kernel's seam (ADR 0054), because the
/// slice that reads it lives in Assist and may not reference Search.
/// </summary>
public sealed class ListKitsToolTests
{
    private sealed class Shop(IReadOnlyList<KitTemplateView> kits) : IQueryDispatcher
    {
        public ListKitsQuery? Asked { get; private set; }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        {
            Asked = (ListKitsQuery)(object)query;
            return Task.FromResult((TResult)(object)kits);
        }
    }

    private static readonly KitTemplateView Kitchen = new(
        "KITCHEN_STARTER", "Kit de cocina",
        [new KitTemplateSlotView("PAN", "Sartén", true), new KitTemplateSlotView("BOARD", "Tabla", false)],
        [new KitRequirementOption("INDUCTION:true", "Inducción")]);

    private static readonly KitTemplateView Coffee = new("COFFEE_CORNER", "Rincón del café", [new KitTemplateSlotView("MAKER", "Cafetera", true)], []);

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>
    /// No agent is handed it: Tino and Steward each take every read of their own
    /// audience, and a kit's list given to Tino would change what Tino does
    /// without its golden set saying so.
    /// </summary>
    [Fact]
    public void It_is_a_pages_read_that_no_agent_is_handed()
    {
        var tool = new ListKitsTool(new Shop([]));

        Assert.Equal((ToolAudience.Page, "list_kits"), (tool.Audience, tool.Name));
    }

    [Fact]
    public async Task It_reads_the_kits_in_the_language_asked_with_what_each_can_be_asked_to_work_with()
    {
        var shop = new Shop([Kitchen, Coffee]);

        var answer = await new ListKitsTool(shop).ReadAsync(Arguments("""{"culture":"en"}"""), TestContext.Current.CancellationToken);

        Assert.Equal("en", shop.Asked!.Culture);
        var kits = Assert.IsType<KitsOnSale>(answer.Data);
        Assert.Equal(["KITCHEN_STARTER", "COFFEE_CORNER"], kits.Kits.Select(kit => kit.Code));
        Assert.Equal(("Kit de cocina", new KitRequirementOption("INDUCTION:true", "Inducción")),
            (kits.Kits[0].Name, Assert.Single(kits.Kits[0].Requirements)));
        Assert.Empty(kits.Kits[1].Requirements);
        Assert.Equal((2, (int?)2), (answer.Rows, answer.Total));
    }

    [Fact]
    public async Task With_no_language_it_reads_the_shops_own()
    {
        var shop = new Shop([Kitchen]);

        await new ListKitsTool(shop).ReadAsync(Arguments("{}"), TestContext.Current.CancellationToken);

        Assert.Equal("es", shop.Asked!.Culture);
    }
}
