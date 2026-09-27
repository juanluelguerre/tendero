using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Ordering.Features.CheckoutSessions;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using ElGuerre.Tendero.Tests.Ucp;
using ElGuerre.Tendero.Ucp.Ap2;
using ElGuerre.Tendero.Ucp.Capabilities;
using ElGuerre.Tendero.Ucp.Checkout;
using ElGuerre.Tendero.Ucp.Negotiation;
using ElGuerre.Tendero.Ucp.Signing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// Checkout's operations once AP2 is negotiated (P11-4), over a scripted
/// dispatcher: every checkout comes back signed and valid against the extension's
/// schema, and completion dispatches nothing until a mandate approves these terms.
/// </summary>
[Collection(UcpSchemas.Collection)]
public sealed class UcpCheckoutAp2Tests : IDisposable
{
    private const string Agent = "claude-desktop-test";
    private const string WithAp2 = "https://ucp.dev/schemas/common/payment_ap2_mandate.json#/$defs/dev.ucp.shopping.checkout";

    private static readonly UcpCompleteRequest Pay = new()
    {
        Payment = new UcpPaymentRequest
        {
            Instruments = [new UcpInstrumentRequest { HandlerId = UcpCheckoutMapper.SandboxHandlerId, Credential = new UcpCredentialRequest { Token = "tok_ok" } }],
        },
    };

    private readonly TestClock clock = new();
    private readonly Ap2MandateTests.TestMerchant merchant = new();
    private readonly DevelopmentPlatforms platforms;
    private readonly ServiceProvider services;
    private readonly Scripted dispatcher = new();

    public UcpCheckoutAp2Tests()
    {
        platforms = new DevelopmentPlatforms(clock);
        services = new ServiceCollection()
            .AddSingleton<IMerchantSigner>(merchant)
            .AddKeyedSingleton<ICheckoutMandateVerifier, ClosedCheckoutMandateVerifier>(ClosedCheckoutMandateVerifier.Vct)
            .BuildServiceProvider();
    }

    private UcpCheckoutOperations Operations => new(dispatcher, dispatcher, merchant, new CheckoutMandates(services, clock));

    private UcpCheckoutCall Call(bool ap2 = true) => new(
        new UcpNegotiated(
            new HashSet<string>(ap2
                ? [CheckoutCapability.Name, FulfillmentCapability.Name, DiscountCapability.Name, Ap2MandateCapability.Name]
                : [CheckoutCapability.Name, FulfillmentCapability.Name, DiscountCapability.Name]),
            new PlatformProfile(new Dictionary<string, IReadOnlySet<string>>()) { Keys = [platforms.KeyOf(Agent)] }),
        UcpCheckoutConformanceTests.Links);

    private static Guid Id => Guid.Parse(UcpCheckoutConformanceTests.Session(CheckoutState.ReadyForComplete).Id);

    private string MandateOver(object checkout) =>
        platforms.SignCheckoutMandate(Agent, (JsonObject)checkout, merchant.PublishedKeys, "https://api.shop.example")!;

    [Fact]
    public async Task Every_checkout_is_signed_and_validates_against_the_extension_schema()
    {
        dispatcher.Session = UcpCheckoutConformanceTests.Session(CheckoutState.ReadyForComplete);

        var answer = await Operations.GetAsync(Id, "es", Call(), TestContext.Current.CancellationToken);

        var checkout = Assert.IsType<JsonObject>(answer.Body);
        Assert.True(MerchantAuthorization.Verify(checkout, merchant.PublishedKeys));
        UcpSchemas.Validate(WithAp2, JsonSerializer.SerializeToElement(checkout), "A signed checkout");
        Assert.Contains(Ap2MandateCapability.Name, checkout["ucp"]!["capabilities"]!.AsObject().Select(member => member.Key));
    }

    [Fact]
    public async Task Without_ap2_nothing_is_signed_and_no_mandate_is_asked_for()
    {
        dispatcher.Session = UcpCheckoutConformanceTests.Session(CheckoutState.ReadyForComplete);

        var got = await Operations.GetAsync(Id, "es", Call(ap2: false), TestContext.Current.CancellationToken);
        await Operations.CompleteAsync(Id, Pay, "key-1", "es", Call(ap2: false), TestContext.Current.CancellationToken);

        Assert.IsType<UcpCheckout>(got.Body);
        Assert.Null(Assert.Single(dispatcher.Completions).Mandate);
    }

    [Fact]
    public async Task A_mandate_over_these_terms_completes_the_checkout()
    {
        dispatcher.Session = UcpCheckoutConformanceTests.Session(CheckoutState.ReadyForComplete);
        var shown = (await Operations.GetAsync(Id, "es", Call(), TestContext.Current.CancellationToken)).Body;

        await Operations.CompleteAsync(Id, Pay with { Ap2 = new UcpAp2CompleteRequest { CheckoutMandate = MandateOver(shown) } }, "key-1", "es", Call(), TestContext.Current.CancellationToken);

        var completion = Assert.Single(dispatcher.Completions);
        Assert.Equal("tok_ok", completion.Instrument);

        // What verified travels on as values (P11-5): the kind, the platform key,
        // the approval's hash, and the total the person approved — 148.85 EUR.
        var scope = completion.Mandate!;
        Assert.Equal((ClosedCheckoutMandateVerifier.Vct, platforms.KeyOf(Agent).KeyId, new Money(148.85m, "EUR")),
            (scope.Kind, scope.Issuer, scope.ApprovedTotal));
        Assert.Equal(43, scope.Reference.Length);
        Assert.NotNull(scope.ExpiresAt);
    }

    [Fact]
    public async Task No_mandate_or_a_mandate_over_other_terms_completes_nothing_and_says_which()
    {
        dispatcher.Session = UcpCheckoutConformanceTests.Session(CheckoutState.ReadyForComplete);
        var shown = (await Operations.GetAsync(Id, "es", Call(), TestContext.Current.CancellationToken)).Body;

        var missing = await Operations.CompleteAsync(Id, Pay, "key-1", "es", Call(), TestContext.Current.CancellationToken);

        // The agent changed the basket after the person approved it.
        dispatcher.Session = dispatcher.Session with { Lines = [dispatcher.Session.Lines[0] with { Quantity = 3, Total = 239.85m }] };
        var moved = await Operations.CompleteAsync(Id, Pay with { Ap2 = new UcpAp2CompleteRequest { CheckoutMandate = MandateOver(shown) } }, "key-2", "es", Call(), TestContext.Current.CancellationToken);

        Assert.Empty(dispatcher.Completions);
        Assert.Equal((Ap2ErrorCodes.MandateRequired, "recoverable"), Error(missing));
        Assert.Equal((Ap2ErrorCodes.MandateScopeMismatch, "requires_buyer_review"), Error(moved));

        // In the checkout's language, like every other message it carries (invariant 6).
        Assert.StartsWith("Se negoció AP2", ((JsonObject)missing.Body)["messages"]!.AsArray().Last()!["content"]!.GetValue<string>(), StringComparison.Ordinal);

        // The refusal is the current checkout, signed afresh, so the platform can ask again.
        Assert.True(MerchantAuthorization.Verify((JsonObject)moved.Body, merchant.PublishedKeys));
        UcpSchemas.Validate(WithAp2, JsonSerializer.SerializeToElement(moved.Body), "A refused completion");
    }

    private static (string?, string?) Error(UcpAnswer answer)
    {
        var message = ((JsonObject)answer.Body)["messages"]!.AsArray().Last()!;
        return (message["code"]?.GetValue<string>(), message["severity"]?.GetValue<string>());
    }

    public void Dispose()
    {
        services.Dispose();
        platforms.Dispose();
        merchant.Dispose();
    }

    /// <summary>Answers every command and query with the session it holds, and remembers the completions it was sent.</summary>
    private sealed class Scripted : ICommandDispatcher, IQueryDispatcher
    {
        public CheckoutSessionView Session { get; set; } = UcpCheckoutConformanceTests.Session(CheckoutState.ReadyForComplete);

        public List<CompleteCheckoutSessionCommand> Completions { get; } = [];

        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        {
            if (command is CompleteCheckoutSessionCommand completion)
                Completions.Add(completion);
            return Task.FromResult((TResult)(object)new CheckoutSessionResult(CheckoutSessionOutcome.Ok, Session));
        }

        public Task<TResult> SendAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
            Task.FromResult((TResult)(object)new CheckoutSessionResult(CheckoutSessionOutcome.Ok, Session));
    }
}
