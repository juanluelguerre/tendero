using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// The cart's rules, the ones a screen cannot be trusted to enforce.
///
/// The load-bearing one is what the cart does NOT hold: there is no price on a
/// line and no total on the aggregate, so there is nothing here to test about
/// money. That absence is checked at the type level — `CartLine` has no `Money`
/// field — and it is why every figure a shopper sees comes from a live quote.
/// </summary>
public sealed class CartTests
{
    private static readonly TestClock Clock = new();

    private static Cart Open() => Cart.Start(Clock, "es", "EUR");

    private static CartLine Line(string sku, int quantity = 1) =>
        new(ProductId.New(), VariantId.New(), sku, "A product", null, null, quantity);

    // ---------- Adding ----------

    [Fact]
    public void Adding_the_same_sku_twice_is_one_line_with_more_of_it()
    {
        var cart = Open();

        cart.Add(Clock, Line("PANS", 2));
        cart.Add(Clock, Line("PANS", 3));

        var line = Assert.Single(cart.Lines);
        Assert.Equal(("PANS", 5), (line.Sku, line.Quantity));
    }

    /// <summary>
    /// Not tidiness. `QuoteCart` refuses a repeated SKU outright — a quote whose
    /// lines do not match the caller's could never have its fingerprint verified
    /// at checkout — so a cart that produced two lines for one SKU could never
    /// be priced.
    /// </summary>
    [Fact]
    public void A_sku_appears_once_however_it_was_added()
    {
        var cart = Open();

        cart.Add(Clock, Line("PANS"));
        cart.Add(Clock, Line("pans"));
        cart.Add(Clock, Line("PANS"));

        Assert.Equal(["PANS"], cart.Lines.Select(line => line.Sku));
        Assert.Equal(3, cart.ItemCount);
    }

    [Fact]
    public void A_line_needs_a_positive_quantity()
    {
        var cart = Open();

        Assert.Throws<InvalidOperationException>(() => cart.Add(Clock, Line("PANS", 0)));
        Assert.Throws<InvalidOperationException>(() => cart.Add(Clock, Line("PANS", -1)));
    }

    /// <summary>
    /// The cart endpoints stay anonymous, so the caps are the only thing between
    /// an open POST and an unbounded row. They belong on the aggregate rather
    /// than in a validator for exactly that reason: a second caller — an agent
    /// over UCP — must not have to remember them.
    /// </summary>
    [Fact]
    public void A_cart_holds_a_bounded_number_of_lines_and_units()
    {
        var cart = Open();

        for (var i = 0; i < Cart.MaximumLines; i++)
            cart.Add(Clock, Line($"SKU-{i}"));

        Assert.Throws<InvalidOperationException>(() => cart.Add(Clock, Line("ONE-TOO-MANY")));
        Assert.Throws<InvalidOperationException>(
            () => cart.Add(Clock, Line("SKU-0", Cart.MaximumQuantity)));
    }

    // ---------- Changing ----------

    /// <summary>
    /// Zero removes. Pressing "−" at one unit means removing it, and asking the
    /// interface to call a different operation for the last one is how
    /// off-by-one bugs get written.
    /// </summary>
    [Fact]
    public void Setting_a_quantity_to_zero_removes_the_line()
    {
        var cart = Open();
        cart.Add(Clock, Line("PANS", 3));
        cart.Add(Clock, Line("SHOES", 1));

        cart.SetQuantity(Clock, "PANS", 0);

        Assert.Equal(["SHOES"], cart.Lines.Select(line => line.Sku));
    }

    [Fact]
    public void Changing_a_line_that_is_not_there_says_so()
    {
        var cart = Open();

        Assert.Throws<InvalidOperationException>(() => cart.SetQuantity(Clock, "GHOST", 2));
    }

    [Fact]
    public void Removing_is_setting_the_quantity_to_zero()
    {
        var cart = Open();
        cart.Add(Clock, Line("PANS"));

        cart.Remove(Clock, "PANS");

        Assert.True(cart.IsEmpty);
    }

    // ---------- Pricing hand-off ----------

    /// <summary>
    /// The order is stable because the quote's fingerprint is computed over
    /// these lines. A fingerprint that changed when two lines swapped places
    /// would reject a checkout for no reason a shopper could understand.
    /// </summary>
    [Fact]
    public void The_lines_reach_pricing_in_a_stable_order()
    {
        var cart = Open();
        cart.Add(Clock, Line("PANS", 2));
        cart.Add(Clock, Line("APRON", 1));
        cart.Add(Clock, Line("SHOES", 4));

        Assert.Equal(
            [("APRON", 1), ("PANS", 2), ("SHOES", 4)],
            cart.PricingLines());
    }

    // ---------- Identity ----------

    /// <summary>
    /// Whoever presents the token sees the cart, so it has to be unguessable.
    /// A GUID would be neither: sequential in v7, and 122 bits of it are not
    /// random at all.
    /// </summary>
    [Fact]
    public void Every_cart_gets_its_own_unguessable_token()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => Open().Token).ToList();

        Assert.Equal(50, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, token =>
        {
            // 32 bytes, base64url, unpadded.
            Assert.Equal(43, token.Length);
            Assert.DoesNotContain('+', token);
            Assert.DoesNotContain('/', token);
            Assert.DoesNotContain('=', token);
        });
    }

    [Fact]
    public void A_guest_cart_becomes_a_customers_when_they_sign_in()
    {
        var cart = Open();
        var customer = CustomerId.New();

        Assert.Null(cart.CustomerId);

        cart.Claim(Clock, customer);

        Assert.Equal(customer, cart.CustomerId);

        // The token keeps working. Revoking it mid-session would empty the
        // basket of anybody whose sign-in did not complete.
        Assert.NotNull(cart.Token);
    }

    [Fact]
    public void A_cart_that_already_belongs_to_somebody_is_not_taken_over()
    {
        var cart = Open();
        cart.Claim(Clock, CustomerId.New());

        Assert.Throws<InvalidOperationException>(() => cart.Claim(Clock, CustomerId.New()));
    }

    [Fact]
    public void Claiming_the_same_cart_twice_is_harmless()
    {
        var cart = Open();
        var customer = CustomerId.New();

        cart.Claim(Clock, customer);
        cart.Claim(Clock, customer);

        Assert.Equal(customer, cart.CustomerId);
    }

    // ---------- Lifecycle ----------

    [Fact]
    public void Checking_out_closes_the_cart_for_good()
    {
        var cart = Open();
        cart.Add(Clock, Line("PANS"));

        cart.MarkCheckedOut(Clock);

        Assert.Equal(CartStatus.CheckedOut, cart.Status);
        Assert.Throws<InvalidOperationException>(() => cart.Add(Clock, Line("SHOES")));
        Assert.Throws<InvalidOperationException>(() => cart.MarkCheckedOut(Clock));
        Assert.Throws<InvalidOperationException>(() => cart.Abandon(Clock));
    }

    [Fact]
    public void An_empty_cart_cannot_become_an_order()
    {
        Assert.Throws<InvalidOperationException>(() => Open().MarkCheckedOut(Clock));
    }

    /// <summary>
    /// The clock measures neglect, not age: every change buys another week. A
    /// cart somebody is actively editing must not expire under them.
    /// </summary>
    [Fact]
    public void Every_change_pushes_the_expiry_out()
    {
        var clock = new TestClock();
        var cart = Cart.Start(clock, "es", "EUR");
        var first = cart.ExpiresAt;

        clock.Advance(TimeSpan.FromDays(3));
        cart.Add(clock, Line("PANS"));

        Assert.Equal(first + TimeSpan.FromDays(3), cart.ExpiresAt);
        Assert.False(cart.HasExpiredAt(clock.GetUtcNow()));
    }

    [Fact]
    public void An_untouched_cart_expires_and_a_closed_one_never_does()
    {
        var clock = new TestClock();
        var cart = Cart.Start(clock, "es", "EUR");
        cart.Add(clock, Line("PANS"));

        var afterwards = clock.GetUtcNow() + Cart.Lifetime;

        Assert.True(cart.HasExpiredAt(afterwards));

        cart.MarkCheckedOut(clock);

        // Expiry is a property of an OPEN cart. An order does not go stale.
        Assert.False(cart.HasExpiredAt(afterwards));
    }
}
