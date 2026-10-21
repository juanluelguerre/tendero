using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ordering.Features.ManageWallet;

// A signed-in customer's saved ways to pay (P15-4, docs/specs/shopper-account.md):
// read, keep a card, remove one, choose the default. The rules are the
// aggregate's (Wallet), the card's keeping and forgetting the provider's; this
// slice asks the one and records the other.
//
// **The token never leaves the server.** What the wire carries is what may be
// shown — brand, last four, expiry — and an id the shop minted. Checkout charges a
// saved method by that id, and only there is the token read back.
//
// **The customer is the caller, always**, and a person, never an agent: agents
// use none of this in phase 15 (decision 8).

// ---------- The wire ----------

/// <param name="Id">The method's identity, flat, as the routes and checkout take it.</param>
/// <param name="Expired">Whether the card is past its month now: said in the wallet, never preselected at checkout.</param>
public sealed record SavedMethodView(string Id, string Brand, string Last4, int ExpiryMonth, int ExpiryYear, bool Expired, bool IsDefault);

/// <param name="Methods">Oldest first, at most <paramref name="MaxMethods"/>; a method waiting to be forgotten is not one of them.</param>
public sealed record WalletView(IReadOnlyList<SavedMethodView> Methods, int MaxMethods)
{
    public static readonly WalletView Empty = new([], Domain.Wallet.MaxMethods);

    public static WalletView Of(Domain.Wallet? wallet, DateTimeOffset now) => wallet is null
        ? Empty
        : new(
            [.. wallet.Methods.Select(method => new SavedMethodView(
                method.Id.ToString(), method.Brand, method.Last4, method.ExpiryMonth, method.ExpiryYear,
                method.HasExpiredAt(now), wallet.Default == method.Id))],
            Domain.Wallet.MaxMethods);
}

/// <param name="Instrument">What the provider's own card field produced: a one-time instrument, never a card number.</param>
public sealed record KeepCardRequest(string? Instrument, bool MakeDefault);

// ---------- Commands and the query ----------

public sealed record GetWalletQuery : IQuery<WalletView>;

public sealed record KeepCardCommand([property: AuditRedacted] string Instrument, bool MakeDefault) : ICommand<WalletResult>;

public sealed record RemoveCardCommand(SavedMethodId Id) : ICommand<WalletResult>;

public sealed record UseCardByDefaultCommand(SavedMethodId Id) : ICommand<WalletResult>;

/// <summary>What a change to the wallet came to. A closed set: the endpoint names every case.</summary>
public enum WalletOutcome
{
    Done,
    NoCustomer,
    Full,
    UnknownMethod,
    /// <summary>The provider would not keep the card.</summary>
    CardRefused,
    /// <summary>The provider could not be reached. Retryable.</summary>
    ProviderUnavailable,
}

/// <param name="Reason">The provider's sentence, when it refused: for a log, never a screen.</param>
public sealed record WalletResult(WalletOutcome Outcome, WalletView View, string? Reason = null) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        WalletOutcome.Done => AuditOutcome.Allowed,
        WalletOutcome.NoCustomer or WalletOutcome.Full or WalletOutcome.UnknownMethod
            or WalletOutcome.CardRefused or WalletOutcome.ProviderUnavailable => AuditOutcome.Denied,
    };

    string? IAuditedResult.AuditReason => Outcome == WalletOutcome.Done ? null : Outcome.ToString();
}

// ---------- Refusals ----------

public static class WalletRefusals
{
    public static readonly Refusal NoCustomer = new(
        "NoCustomer", 409, "Tu cuenta todavía no está lista. Vuelve a entrar.", "Your account is not ready yet. Sign in again.");

    public static readonly Refusal Full = new(
        "WalletFull", 409,
        $"Ya tienes {Domain.Wallet.MaxMethods} formas de pago. Quita una para guardar otra.",
        $"You already have {Domain.Wallet.MaxMethods} ways to pay. Remove one to keep another.");

    public static readonly Refusal UnknownMethod = new(
        "UnknownPaymentMethod", 404, "Esa forma de pago no está guardada en tu cuenta.", "That way to pay is not saved in your account.");

    public static readonly Refusal CardRefused = new(
        "CardRefused", 400, "El proveedor de pagos no ha podido guardar esta tarjeta.", "The payment provider could not keep this card.");

    public static readonly Refusal ProviderUnavailable = new(
        "PaymentProviderUnavailable", 503,
        "El proveedor de pagos no responde. Inténtalo de nuevo en un momento.",
        "The payment provider did not answer. Try again in a moment.");
}

// ---------- Validation ----------

public sealed class KeepCardValidator : AbstractValidator<KeepCardCommand>
{
    public KeepCardValidator() => RuleFor(command => command.Instrument).NotEmpty().MaximumLength(100).OverridePropertyName("instrument");
}

// ---------- Endpoints ----------

public sealed class ManageWalletEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var methods = app.MapGroup("/api/accounts/me/payment-methods")
            .RequireAuthorization(TenderoPolicyNames.Shopper)
            .WithTags("Ordering");

        methods.MapGet("",
            async Task<Ok<WalletView>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetWalletQuery(), ct)))
            .WithName("GetWallet");

        methods.MapPost("",
            async Task<Results<Ok<WalletView>, ProblemHttpResult>> (KeepCardRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Answer(await dispatcher.SendAsync(new KeepCardCommand(request.Instrument ?? "", request.MakeDefault), ct)))
            .WithName("KeepCard")
            .WithMetadata(new Refuses(WalletRefusals.NoCustomer, WalletRefusals.Full, WalletRefusals.CardRefused, WalletRefusals.ProviderUnavailable));

        // Removed from the wallet either way: forgotten at the provider now, or set
        // aside for a worker when the provider cannot be reached (WalletOutcome.Done).
        methods.MapDelete("/{id:guid}",
            async Task<Results<Ok<WalletView>, ProblemHttpResult>> (Guid id, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Answer(await dispatcher.SendAsync(new RemoveCardCommand(new SavedMethodId(id)), ct)))
            .WithName("RemoveCard")
            .WithMetadata(new Refuses(WalletRefusals.NoCustomer, WalletRefusals.UnknownMethod));

        methods.MapPut("/{id:guid}/default",
            async Task<Results<Ok<WalletView>, ProblemHttpResult>> (Guid id, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Answer(await dispatcher.SendAsync(new UseCardByDefaultCommand(new SavedMethodId(id)), ct)))
            .WithName("UseCardByDefault")
            .WithMetadata(new Refuses(WalletRefusals.NoCustomer, WalletRefusals.UnknownMethod));
    }

    private static Results<Ok<WalletView>, ProblemHttpResult> Answer(WalletResult result) => result.Outcome switch
    {
        WalletOutcome.Done => TypedResults.Ok(result.View),
        WalletOutcome.NoCustomer => Problem(WalletRefusals.NoCustomer),
        WalletOutcome.Full => Problem(WalletRefusals.Full),
        WalletOutcome.UnknownMethod => Problem(WalletRefusals.UnknownMethod),
        WalletOutcome.CardRefused => Problem(WalletRefusals.CardRefused, result.Reason),
        WalletOutcome.ProviderUnavailable => Problem(WalletRefusals.ProviderUnavailable, result.Reason),
    };

    private static ProblemHttpResult Problem(Refusal refusal, string? reason = null) =>
        TypedResults.Problem(statusCode: refusal.Status, extensions: refusal.Said(reason: reason));
}

// ---------- Handlers ----------

public sealed class GetWalletHandler(IWalletReader wallets, IPrincipalAccessor principals, TimeProvider clock)
    : IQueryHandler<GetWalletQuery, WalletView>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<WalletView> HandleAsync(GetWalletQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("ordering.wallet.read");

        if (principals.Current.Customer is not { } customer)
        {
            activity?.SetTag("ordering.unlinked", true);
            return WalletView.Empty;
        }

        var wallet = await wallets.ForCustomerAsync(customer, cancellationToken);
        activity?.SetTag("ordering.wallet.methods", wallet?.Methods.Count ?? 0);

        return WalletView.Of(wallet, clock.GetUtcNow());
    }
}

public sealed class ManageWalletHandlers(
    IWalletRepository wallets,
    IPaymentProviderRegistry payments,
    IOptions<CheckoutOptions> options,
    IPrincipalAccessor principals,
    IUnitOfWork unitOfWork,
    TimeProvider clock) :
    ICommandHandler<KeepCardCommand, WalletResult>,
    ICommandHandler<RemoveCardCommand, WalletResult>,
    ICommandHandler<UseCardByDefaultCommand, WalletResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    /// <summary>Full is refused before the provider is asked: a card kept and then not recorded would be a token nobody can forget.</summary>
    public Task<WalletResult> HandleAsync(KeepCardCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("ordering.wallet.keep", create: true, async wallet =>
        {
            if (wallet.IsFull)
                return (WalletOutcome.Full, null);

            // The provider a live order would pay through: a kept card is charged by the same one.
            var key = options.Value.ProviderFor(OrderModes.Of(principals.Current));
            var saving = await payments.Get(key).SaveInstrumentAsync(command.Instrument, cancellationToken);

            if (saving.Outcome != InstrumentSavingOutcome.Saved || saving.Instrument is not { } card)
                return (saving.Outcome == InstrumentSavingOutcome.Unavailable ? WalletOutcome.ProviderUnavailable : WalletOutcome.CardRefused, saving.Reason);

            wallet.Add(clock, key, new CardOnFile(card.Token, card.Brand, card.Last4, card.ExpiryMonth, card.ExpiryYear), command.MakeDefault);
            return (WalletOutcome.Done, null);
        }, cancellationToken);

    /// <summary>
    /// Forgotten at the provider before the method goes; when the provider cannot
    /// be reached, set aside — out of the wallet and out of checkout now — for the
    /// worker to forget. Either way the shopper sees it gone.
    /// </summary>
    public Task<WalletResult> HandleAsync(RemoveCardCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("ordering.wallet.remove", create: false, async wallet =>
        {
            if (wallet.Find(command.Id) is not { } method)
                return (WalletOutcome.UnknownMethod, null);

            var forgetting = await payments.Get(method.ProviderKey).ForgetInstrumentAsync(method.Token, cancellationToken);
            Activity.Current?.SetTag("ordering.wallet.forgotten_now", forgetting.Succeeded);

            if (forgetting.Succeeded)
                wallet.Remove(clock, command.Id);
            else
                wallet.SetAsideForForgetting(clock, command.Id);

            return (WalletOutcome.Done, null);
        }, cancellationToken);

    public Task<WalletResult> HandleAsync(UseCardByDefaultCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("ordering.wallet.default", create: false, wallet =>
            Task.FromResult<(WalletOutcome, string?)>(wallet.UseByDefault(clock, command.Id) ? (WalletOutcome.Done, null) : (WalletOutcome.UnknownMethod, null)),
            cancellationToken);

    /// <summary>
    /// Load the caller's wallet — starting one when <paramref name="create"/> says
    /// the change needs somewhere to go — apply the change, save only when it was
    /// applied, and answer the wallet as it is now. Never a token on the span.
    /// </summary>
    private async Task<WalletResult> ChangeAsync(
        string span, bool create, Func<Domain.Wallet, Task<(WalletOutcome Outcome, string? Reason)>> change, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity(span);

        if (principals.Current.Customer is not { } customer)
        {
            activity?.SetTag("ordering.unlinked", true);
            return new WalletResult(WalletOutcome.NoCustomer, WalletView.Empty);
        }

        var wallet = await wallets.FindAsync(customer, cancellationToken);
        var started = false;

        if (wallet is null)
        {
            if (!create)
                return new WalletResult(WalletOutcome.UnknownMethod, WalletView.Empty);

            wallet = Domain.Wallet.StartFor(customer, clock);
            started = true;
        }

        var (outcome, reason) = await change(wallet);
        activity?.SetTag("ordering.wallet.outcome", outcome.ToString());

        if (outcome == WalletOutcome.Done)
        {
            if (started)
                wallets.Add(wallet);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        activity?.SetTag("ordering.wallet.methods", wallet.Methods.Count);
        return new WalletResult(outcome, WalletView.Of(started && outcome != WalletOutcome.Done ? null : wallet, clock.GetUtcNow()), reason);
    }
}
