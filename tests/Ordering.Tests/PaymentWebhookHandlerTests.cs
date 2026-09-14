using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Features.PaymentWebhook;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using ElGuerre.Tendero.Tests;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ElGuerre.Tendero.Ordering.Tests;

/// <summary>
/// What the shop does with a webhook once the provider has judged its
/// signature.
///
/// The signature itself is the provider's business and its contract suite
/// covers it. This is the other half, and it had no test at all: the handler is
/// where a message from a stranger's server turns into an order marked paid or
/// failed. Every refusal here is a way of NOT letting it — a forgery, a replay,
/// an order that does not exist, a failure that arrives after the order was
/// already confirmed — and each one has to leave the order untouched and
/// nothing saved.
/// </summary>
public sealed class PaymentWebhookHandlerTests
{
    private readonly TestClock clock = new();
    private readonly ScriptedProvider provider = new();
    private readonly InMemoryOrders orders = new();
    private readonly CountingUnitOfWork unitOfWork = new();
    private readonly RecordingLogger logger = new();

    private Task<PaymentWebhookResult> Deliver() =>
        new PaymentWebhookHandler(
                new SingleProvider(this.provider), this.orders, this.unitOfWork, this.clock,
                this.logger)
            .HandleAsync(
                new PaymentWebhookCommand(ScriptedProvider.ProviderKey, "{}", new Dictionary<string, string>()),
                CancellationToken.None);

    private Order AuthorisedOrder()
    {
        var order = OrderBuilder.Default().Build();
        order.AuthorizePayment(this.clock, new OrderPayment(ScriptedProvider.ProviderKey, "auth_1", null));
        this.orders.Add(order);
        return order;
    }

    private void ProviderSays(string kind, OrderId? orderId, string reference = "cap_1", string? reason = null) =>
        this.provider.Verification = WebhookVerification.Accepted(new PaymentEvent(kind, reference, orderId, reason));

    // ---------- What is refused before any order is read ----------

    [Theory]
    [InlineData(WebhookVerdict.BadSignature)]
    [InlineData(WebhookVerdict.Stale)]
    [InlineData(WebhookVerdict.Ignored)]
    public async Task A_webhook_the_provider_did_not_accept_touches_no_order(WebhookVerdict verdict)
    {
        this.provider.Verification = WebhookVerification.Rejected(verdict, "because");

        var result = await Deliver();

        Assert.Equal(verdict, result.Verdict);
        Assert.Equal("because", result.Detail);
        Assert.Equal(0, this.orders.Lookups);
        Assert.Equal(0, this.unitOfWork.Saves);
    }

    /// <summary>
    /// Replay protection is only as good as the instant it is measured against.
    /// The provider has to be handed the shop's clock, not its own idea of now.
    /// </summary>
    [Fact]
    public async Task The_provider_judges_freshness_against_the_shops_clock()
    {
        this.provider.Verification = WebhookVerification.Rejected(WebhookVerdict.Stale, "old");

        await Deliver();

        Assert.Equal(this.clock.GetUtcNow(), this.provider.JudgedAt);
    }

    // ---------- Signed and fresh, and still not ours to act on ----------

    [Fact]
    public async Task A_verified_webhook_that_names_no_order_is_ignored()
    {
        ProviderSays(PaymentWebhookHandler.Captured, orderId: null);

        var result = await Deliver();

        Assert.Equal(WebhookVerdict.Ignored, result.Verdict);
        Assert.Equal(0, this.unitOfWork.Saves);
    }

    /// <summary>A signed message can still name an order that does not exist.
    /// It is answered, not trusted: 200 so the provider stops retrying.</summary>
    [Fact]
    public async Task A_verified_webhook_for_an_order_that_does_not_exist_is_ignored()
    {
        var ghost = OrderId.New();
        ProviderSays(PaymentWebhookHandler.Captured, ghost);

        var result = await Deliver();

        Assert.Equal(WebhookVerdict.Ignored, result.Verdict);
        Assert.Contains(ghost.ToString(), result.Detail);
        Assert.Equal(0, this.unitOfWork.Saves);
    }

    [Fact]
    public async Task An_event_kind_the_shop_does_not_act_on_is_ignored()
    {
        var order = AuthorisedOrder();
        ProviderSays("payment.refunded", order.Id);

        var result = await Deliver();

        Assert.Equal(WebhookVerdict.Ignored, result.Verdict);
        Assert.Equal("payment.refunded", result.Detail);
        Assert.Equal(OrderStatus.PaymentAuthorized, order.Status);
        Assert.Null(order.Payment!.CaptureId);
        Assert.Equal(0, this.unitOfWork.Saves);
    }

    // ---------- Captured ----------

    [Fact]
    public async Task A_capture_records_the_providers_reference_on_the_order_and_saves()
    {
        var order = AuthorisedOrder();
        ProviderSays(PaymentWebhookHandler.Captured, order.Id, reference: "cap_42");

        var result = await Deliver();

        Assert.Equal(WebhookVerdict.Accepted, result.Verdict);
        Assert.Equal("cap_42", order.Payment!.CaptureId);
        Assert.Equal(1, this.unitOfWork.Saves);
    }

    // ---------- Failed ----------

    /// <summary>
    /// A payment that fails before the money moved cancels the order, with the
    /// provider's reason under the "payment declined" code — and it does so by
    /// CANCELLING, so the compensation that already exists runs on
    /// <see cref="OrderCancelled"/>: the stock goes back on the shelf and the
    /// hold is voided.
    ///
    /// It used to try to move the order to PaymentFailed, which the table only
    /// allows from Pending — and since checkout authorises before it places
    /// (ADR 0025), no saved order is ever Pending. Every real failure webhook
    /// threw, answered 500, and the provider retried it forever.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.PaymentAuthorized)]
    [InlineData(OrderStatus.Confirmed)]
    public async Task A_failure_before_the_money_moved_cancels_the_order_with_the_providers_reason(
        OrderStatus status)
    {
        var order = AuthorisedOrder();
        if (status == OrderStatus.Confirmed)
            order.Confirm(this.clock);
        ProviderSays(PaymentWebhookHandler.Failed, order.Id, reason: "insufficient funds");

        var result = await Deliver();

        Assert.Equal(WebhookVerdict.Accepted, result.Verdict);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(new OrderStop(OrderStop.PaymentDeclined, "insufficient funds"), order.Stop);
        Assert.Contains(order.DomainEvents, domainEvent => domainEvent is OrderCancelled);
        Assert.Equal(1, this.unitOfWork.Saves);
    }

    [Fact]
    public async Task A_failure_with_no_reason_still_says_why()
    {
        var order = AuthorisedOrder();
        ProviderSays(PaymentWebhookHandler.Failed, order.Id, reason: null);

        await Deliver();

        Assert.Equal(OrderStop.PaymentDeclined, order.Stop!.Code);
        Assert.False(string.IsNullOrWhiteSpace(order.Stop.Detail));
    }

    /// <summary>
    /// Once the capture has landed, "the payment failed" contradicts a fact the
    /// order already holds. Webhooks arrive in no particular order, and this is
    /// almost always an earlier attempt's failure arriving after a later one's
    /// success — so it must not cancel an order whose money is in the bank.
    ///
    /// Not touching the order is not the same as not saying anything. If money
    /// really went back after a capture, that is a refund or a chargeback, and
    /// a person has to be able to find the message that hinted at it.
    /// </summary>
    [Fact]
    public async Task A_failure_after_the_money_was_captured_changes_nothing_and_is_reported()
    {
        var order = AuthorisedOrder();
        order.Confirm(this.clock);
        order.CapturePayment(this.clock, "cap_1");
        ProviderSays(PaymentWebhookHandler.Failed, order.Id, reason: "late");

        var result = await Deliver();

        Assert.Equal(WebhookVerdict.Ignored, result.Verdict);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Null(order.Stop);
        Assert.Equal(0, this.unitOfWork.Saves);

        var warning = Assert.Single(this.logger.Warnings);
        Assert.Contains(order.Id.ToString(), warning);
        Assert.Contains("cap_1", warning);
    }

    /// <summary>
    /// The one the shop cannot fix on its own. Capture happens on shipping
    /// (ADR 0025), and a capture that fails there is left uncaptured — so an
    /// order can be out of the building with no money behind it. It cannot be
    /// cancelled: the goods are gone. Nor is a return the system's to start: a
    /// return is the customer's, per line, and its refund needs a capture that
    /// never happened. What it needs is a person, who will usually ask for the
    /// payment again before asking for the goods back — and the warning is how
    /// that person finds out.
    /// </summary>
    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    public async Task A_failure_on_an_order_that_left_uncaptured_is_left_for_a_person(OrderStatus status)
    {
        var order = AuthorisedOrder();
        order.Confirm(this.clock);
        order.Ship(this.clock);
        if (status == OrderStatus.Delivered)
            order.Deliver(this.clock);
        ProviderSays(PaymentWebhookHandler.Failed, order.Id, reason: "authorisation expired");

        var result = await Deliver();

        Assert.Equal(WebhookVerdict.Ignored, result.Verdict);
        Assert.Equal(status, order.Status);
        Assert.Null(order.Stop);
        Assert.Equal(0, this.unitOfWork.Saves);

        var warning = Assert.Single(this.logger.Warnings);
        Assert.Contains(order.Id.ToString(), warning);
        Assert.Contains("authorisation expired", warning);
    }

    /// <summary>The stock saga may have cancelled it first. Cancelling a
    /// cancelled order is not a transition, so the message is answered and
    /// dropped rather than thrown back at the provider — quietly, because an
    /// order already stopped with its hold already voided needs nobody.</summary>
    [Fact]
    public async Task A_failure_for_an_order_already_cancelled_is_ignored_quietly()
    {
        var order = AuthorisedOrder();
        order.Cancel(this.clock, OrderStop.OutOfStock, "no stock");
        ProviderSays(PaymentWebhookHandler.Failed, order.Id, reason: "declined");

        var result = await Deliver();

        Assert.Equal(WebhookVerdict.Ignored, result.Verdict);
        Assert.Equal(OrderStop.OutOfStock, order.Stop!.Code);
        Assert.Equal(0, this.unitOfWork.Saves);
        Assert.Empty(this.logger.Warnings);
    }

    /// <summary>The ordinary path is not a warning. A log that shouts on every
    /// declined card is a log nobody reads when it matters.</summary>
    [Fact]
    public async Task Cancelling_for_a_failed_payment_is_not_a_warning()
    {
        var order = AuthorisedOrder();
        ProviderSays(PaymentWebhookHandler.Failed, order.Id, reason: "insufficient funds");

        await Deliver();

        Assert.Empty(this.logger.Warnings);
    }

    // ---------- Doubles ----------

    private sealed class ScriptedProvider : IPaymentProvider
    {
        public const string ProviderKey = "scripted";

        public WebhookVerification Verification { get; set; } =
            WebhookVerification.Rejected(WebhookVerdict.BadSignature, "unset");

        public DateTimeOffset? JudgedAt { get; private set; }

        public string Key => ProviderKey;

        public WebhookVerification VerifyWebhook(
            string rawBody, IReadOnlyDictionary<string, string> headers, DateTimeOffset now)
        {
            JudgedAt = now;
            return Verification;
        }

        public Task<PaymentAuthorization> AuthorizeAsync(PaymentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentCapture> CaptureAsync(string authorizationId, Money amount, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentRefund> RefundAsync(string captureId, Money amount, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PaymentVoid> VoidAsync(string authorizationId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SingleProvider(IPaymentProvider provider) : IPaymentProviderRegistry
    {
        public IReadOnlyCollection<string> Keys => [provider.Key];

        public IPaymentProvider Get(string key) =>
            key == provider.Key ? provider : throw new InvalidOperationException($"No provider '{key}'.");
    }

    private sealed class InMemoryOrders : IOrderRepository
    {
        private readonly List<Order> orders = [];

        public int Lookups { get; private set; }

        public Task<Order?> FindByIdAsync(OrderId id, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return Task.FromResult(this.orders.FirstOrDefault(order => order.Id == id));
        }

        public Task<Order?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(this.orders.FirstOrDefault(order => order.IdempotencyKey == key));

        public void Add(Order order) => this.orders.Add(order);
    }

    /// <summary>What the handler wrote at warning level or above, rendered.
    /// A deterministic double rather than a mock: the test reads the sentence a
    /// person would read.</summary>
    private sealed class RecordingLogger : ILogger<PaymentWebhookHandler>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }
}
