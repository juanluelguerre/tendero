using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Ordering;
using ElGuerre.Tendero.Ordering.Features.Checkout;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The checkout's idempotency key where every API that has one puts it: the
/// `Idempotency-Key` header (Stripe's, the IETF draft's, UCP's), with the
/// meaning they give it — one key, one request. It was a member of the body,
/// read as *has this key bought something*: the same key with another basket
/// was answered the first order as though it were the second request's.
///
/// At the edge, where no database is reached: what is refused before anything
/// is dispatched. That one key never places two orders is the order's own
/// unique index, proven on Postgres in `CheckoutLoopTests`.
/// </summary>
public sealed class CheckoutIdempotencyHeaderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object Order(string instrument = "card-ok") => new
    {
        shippingAddress = new { recipientName = "Ana Ruiz", line1 = "Calle Mayor 1", line2 = (string?)null, city = "Madrid", postalCode = "28013", region = (string?)null, countryCode = "ES", phone = (string?)null },
        shippingOptionCode = "standard",
        quoteHash = "H",
        instrument,
    };

    private static (HttpClient Client, InMemoryIdempotencyStore Keys, Counting Dispatcher, IDisposable Owner) Compose()
    {
        var (keys, dispatcher) = (new InMemoryIdempotencyStore(), new Counting());
        var root = new TenderoApiFactory();
        var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddScoped<IIdempotencyStore>(_ => keys);
            services.AddScoped<ICommandDispatcher>(_ => dispatcher);
        }));
        return (factory.CreateClient(), keys, dispatcher, root);
    }

    private static async Task<(HttpStatusCode Status, string Body)> PlaceAsync(HttpClient client, object order, string? key, string cart = "T-1")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/checkout") { Content = JsonContent.Create(order) };
        request.Headers.Add("X-Cart-Token", cart);
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);

        var response = await client.SendAsync(request, Ct);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Placing_an_order_without_the_header_is_refused_before_anything_runs()
    {
        var (client, _, dispatcher, owner) = Compose();
        using var _ = owner;

        var (status, body) = await PlaceAsync(client, Order(), key: null);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Idempotency-Key", body, StringComparison.Ordinal);
        Assert.Equal(0, dispatcher.Sent);
    }

    /// <summary>The key names one request: the same key with another basket is a conflict, and never an order.</summary>
    [Fact]
    public async Task A_key_that_named_another_order_is_a_conflict_and_nothing_runs()
    {
        var (client, keys, dispatcher, owner) = Compose();
        using var _ = owner;
        await keys.ClaimAsync("cart:T-1", "k-1", "another request's fingerprint", Ct);
        await keys.CompleteAsync("cart:T-1", "k-1", 201, "{}", Ct);

        var (status, body) = await PlaceAsync(client, Order(), "k-1");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("IdempotencyKeyReused", JsonNode.Parse(body)!["code"]!.GetValue<string>());
        // The slice named the refusal and nothing else: the host said it, in the language asked.
        Assert.Equal(CommonRefusals.IdempotencyKeyReused.Title.In("es"), JsonNode.Parse(body)!["title"]!.GetValue<string>());
        Assert.EndsWith("/idempotency-key-reused", JsonNode.Parse(body)!["type"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(0, dispatcher.Sent);
    }

    [Fact]
    public async Task The_same_order_arriving_while_the_first_is_being_placed_is_told_so()
    {
        var (client, keys, dispatcher, owner) = Compose();
        using var _ = owner;
        await PlaceAsync(client, Order(), "k-1");
        Assert.Equal(1, dispatcher.Sent);

        // The first attempt was refused by the shop (nothing of it is on sale): its key is free again.
        var (retried, _) = await PlaceAsync(client, Order(), "k-1");
        Assert.Equal((HttpStatusCode.NotFound, 2), (retried, dispatcher.Sent));

        // And one still running holds it.
        await keys.ClaimAsync("cart:T-2", "k-2", Idempotency.Fingerprint("never the same"), Ct);
        dispatcher.WhileRunning = async () =>
        {
            var (status, body) = await PlaceAsync(client, Order(), "k-3");
            Assert.Equal((HttpStatusCode.Conflict, "IdempotencyInProgress"), (status, JsonNode.Parse(body)!["code"]!.GetValue<string>()));
        };
        await PlaceAsync(client, Order(), "k-3");
        Assert.Equal(3, dispatcher.Sent);
    }

    /// <summary>A key is the cart's: two shoppers may choose the same one.</summary>
    [Fact]
    public async Task One_carts_key_is_not_anothers()
    {
        var (client, keys, dispatcher, owner) = Compose();
        using var _ = owner;
        await keys.ClaimAsync("cart:T-1", "k-1", "another request's fingerprint", Ct);
        await keys.CompleteAsync("cart:T-1", "k-1", 201, "{}", Ct);

        var (status, _) = await PlaceAsync(client, Order(), "k-1", cart: "T-2");

        Assert.Equal((HttpStatusCode.NotFound, 1), (status, dispatcher.Sent));
    }

    /// <summary>
    /// **A shelf that is short is a 409 that says what there is** (ADR 0025,
    /// amended): the till refused before any money, and the answer carries each
    /// article with what was asked and what can be bought, beside the problem's
    /// own members — what a screen needs to offer to buy that many. The till's
    /// own decision is proven on Postgres (`CheckoutLoopTests`); this is the
    /// endpoint saying it, which nothing else held.
    /// </summary>
    [Fact]
    public async Task A_shelf_that_is_short_is_a_conflict_that_says_what_there_is()
    {
        var (client, _, dispatcher, owner) = Compose();
        using var _ = owner;
        dispatcher.Answer = new PlaceOrderResult(
            PlaceOrderOutcome.InsufficientStock, null, null, "PAN-26: 2 asked for, 1 available",
            [new StockShortage("PAN-26", 2, 1)]);

        var (status, body) = await PlaceAsync(client, Order(), "k-short");
        var problem = JsonNode.Parse(body)!;

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("InsufficientStock", problem["code"]!.GetValue<string>());
        Assert.Equal(CheckoutRefusals.InsufficientStock.Title.In("es"), problem["title"]!.GetValue<string>());

        var shortage = Assert.Single(problem["shortages"]!.AsArray())!;
        Assert.Equal(("PAN-26", 2, 1), (shortage["sku"]!.GetValue<string>(), shortage["requested"]!.GetValue<int>(), shortage["available"]!.GetValue<int>()));

        // Refused, so the key is free: the shopper takes one out and pays again with it.
        dispatcher.Answer = null;
        var (again, _) = await PlaceAsync(client, Order(), "k-short");
        Assert.Equal((HttpStatusCode.NotFound, 2), (again, dispatcher.Sent));
    }

    /// <summary>Answers that there is no such cart — or what it is told to — counting; and can ask something of the test while it runs.</summary>
    private sealed class Counting : ICommandDispatcher
    {
        public int Sent { get; private set; }

        public Func<Task>? WhileRunning { get; set; }

        public PlaceOrderResult? Answer { get; set; }

        public async Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            Sent++;
            if (WhileRunning is { } inside)
            {
                WhileRunning = null;
                await inside();
            }

            return (TResult)(object)(Answer ?? new PlaceOrderResult(PlaceOrderOutcome.UnknownSkus, null, null, "MUG-1"));
        }
    }
}
