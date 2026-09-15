using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.ManageCart;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// A basket reads in the language it is asked in (invariant 6).
///
/// Found in a browser on 2026-09-15, with every test green: a pan added from the
/// English storefront stayed "26 cm cast iron frying pan" in the Spanish basket,
/// because the line froze ONE string in the language of the request that added
/// it. The line keeps every translation now, and the view picks one.
/// </summary>
public sealed class CartViewTests
{
    private static readonly TestClock Clock = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The cart is started in the OTHER language on purpose: what it was
    /// begun in is exactly what must not decide what it reads in.</summary>
    [Theory]
    [InlineData("es", "en", "Sartén de hierro fundido de 26 cm", "Negro · 26")]
    [InlineData("en", "es", "26 cm cast iron frying pan", "Black · 26")]
    public async Task A_line_reads_in_the_language_the_cart_is_asked_in(
        string asked, string startedIn, string name, string label)
    {
        var cart = Cart.Start(Clock, startedIn, "EUR");
        cart.Add(Clock, Pan());

        var view = await new GetCartHandler(new Carts(cart)).HandleAsync(new GetCartQuery(cart.Token, asked), Ct);

        var line = Assert.Single(view.Lines);
        Assert.Equal((name, label), (line.ProductName, line.VariantLabel));
    }

    /// <summary>
    /// A name the catalogue only has in one language is still a name: the chain
    /// every <see cref="LocalizedText"/> follows — that culture, then English, then
    /// whatever there is — rather than an empty cell.
    /// </summary>
    [Fact]
    public async Task A_language_the_line_has_no_text_for_falls_back_rather_than_going_blank()
    {
        var cart = Cart.Start(Clock, "es", "EUR");
        cart.Add(Clock, new CartLine(
            ProductId.New(), VariantId.New(), "ONLY-ES", LocalizedText.From("es", "Solo en castellano"), null, null, 1));

        var view = await new GetCartHandler(new Carts(cart)).HandleAsync(new GetCartQuery(cart.Token, "en"), Ct);

        Assert.Equal("Solo en castellano", Assert.Single(view.Lines).ProductName);
    }

    private static CartLine Pan() => new(
        ProductId.New(),
        VariantId.New(),
        "B11COO0301-DEFAULT",
        new LocalizedText(new Dictionary<string, string>
        {
            ["es"] = "Sartén de hierro fundido de 26 cm",
            ["en"] = "26 cm cast iron frying pan"
        }),
        new LocalizedText(new Dictionary<string, string> { ["es"] = "Negro · 26", ["en"] = "Black · 26" }),
        null,
        1);

    private sealed class Carts(Cart cart) : ICartRepository
    {
        public Task<Cart?> FindOpenByTokenAsync(string token, CancellationToken cancellationToken = default) =>
            Task.FromResult<Cart?>(token == cart.Token ? cart : null);

        public Task<Cart?> FindByIdAsync(CartId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Cart?>(id == cart.Id ? cart : null);

        public void Add(Cart added) => throw new NotSupportedException();
    }
}
