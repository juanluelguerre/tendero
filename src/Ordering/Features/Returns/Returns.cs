using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Domain;
using ElGuerre.Tendero.Ordering.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ordering.Features.Returns;

// ---------- Asking ----------

public sealed record ReturnLineRequest(string Sku, int Quantity, string Reason, string? Comment);

public sealed record RequestReturnRequest(IReadOnlyList<ReturnLineRequest> Lines);

public sealed record RequestReturnCommand(
    OrderId OrderId, IReadOnlyList<ReturnLineRequest> Lines, string Culture)
    : ICommand<RequestReturnResult>;

public enum RequestReturnOutcome
{
    Opened,
    UnknownOrder,

    /// <summary>Not delivered, or delivered too long ago. One outcome and two
    /// sentences, because a shopper needs to be told which.</summary>
    WindowClosed,

    /// <summary>A SKU that is not on the order, more than was bought, or a
    /// reason outside the closed set.</summary>
    Refused
}

public sealed record RequestReturnResult(
    RequestReturnOutcome Outcome, ReturnRequest? Return, string? Detail);

public sealed class RequestReturnValidator : AbstractValidator<RequestReturnCommand>
{
    public RequestReturnValidator()
    {
        RuleFor(command => command.Lines)
            .NotEmpty().WithMessage("A return needs at least one line.")
            .Must(lines => lines.All(line => line.Quantity > 0))
                .WithMessage("Quantities must be positive.")
            .Must(lines => lines.All(line => Enum.TryParse<ReturnReason>(line.Reason, ignoreCase: true, out _)))
                // The set is closed and the message says what it contains: a
                // caller told only "invalid reason" has to guess, and an agent
                // guesses badly.
                .WithMessage($"A reason is one of {string.Join(", ", Enum.GetNames<ReturnReason>())}.");

        RuleForEach(command => command.Lines)
            .Must(line => line.Comment is null || line.Comment.Length <= 500)
            .WithMessage("A comment is at most 500 characters.");
    }
}

public sealed class RequestReturnHandler(
    IOrderRepository orders,
    IReturnRequestRepository returns,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<RequestReturnCommand, RequestReturnResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<RequestReturnResult> HandleAsync(
        RequestReturnCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("returns.request");
        activity?.SetTag("ordering.order_id", command.OrderId.Value);

        var order = await orders.FindByIdAsync(command.OrderId, cancellationToken);

        if (order is null)
            return new RequestReturnResult(RequestReturnOutcome.UnknownOrder, null, null);

        if (!ReturnRequest.WindowIsOpen(order, clock.GetUtcNow()))
            return new RequestReturnResult(
                RequestReturnOutcome.WindowClosed, null,
                order.Status == OrderStatus.Delivered
                    ? $"The {ReturnRequest.Window.TotalDays:F0}-day return window has closed."
                    : $"This order is {order.Status} and has not been delivered yet.");

        try
        {
            var request = ReturnRequest.Open(clock, order,
            [
                .. command.Lines.Select(line => new ReturnLine(
                    // The variant comes from the ORDER, not from the request: a
                    // caller that named its own variant id could return a
                    // different thing from the one it bought.
                    order.Lines.First(candidate =>
                        string.Equals(candidate.Sku, line.Sku, StringComparison.OrdinalIgnoreCase)).VariantId,
                    line.Sku,
                    line.Quantity,
                    Enum.Parse<ReturnReason>(line.Reason, ignoreCase: true),
                    line.Comment))
            ]);

            returns.Add(request);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            activity?.SetTag("returns.id", request.Id.ToString());

            return new RequestReturnResult(RequestReturnOutcome.Opened, request, null);
        }
        catch (InvalidOperationException exception)
        {
            // The checks live on the aggregate so a second caller — an agent over
            // UCP — cannot forget them. Turning the refusal into an answer is
            // this handler's job.
            return new RequestReturnResult(RequestReturnOutcome.Refused, null, exception.Message);
        }
    }
}

// ---------- Deciding ----------

public enum ReturnDecision { Approve, Reject, Receive, Refund }

public sealed record DecideReturnRequest(string Decision, string? Reason);

/// <summary>
/// The shopkeeper's side of a return, in one command with a closed set of moves.
///
/// One command and not four slices because they are one queue and one screen:
/// approve, reject, mark received, refund. The transition table is what stops
/// the enum being a free-for-all — receiving something that was never approved
/// throws, and the handler does not have to know that.
/// </summary>
public sealed record DecideReturnCommand(ReturnRequestId ReturnId, ReturnDecision Decision, string? Reason)
    : ICommand<DecideReturnResult>;

public enum DecideReturnOutcome { Decided, UnknownReturn, IllegalTransition, NothingToRefund, RefundFailed }

public sealed record DecideReturnResult(
    DecideReturnOutcome Outcome, ReturnRequest? Return, string? Detail);

public sealed class DecideReturnHandler(
    IReturnRequestRepository returns,
    IOrderRepository orders,
    IPaymentProviderRegistry payments,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<DecideReturnCommand, DecideReturnResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<DecideReturnResult> HandleAsync(
        DecideReturnCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("returns.decide");
        activity?.SetTag("returns.decision", command.Decision.ToString());

        var request = await returns.FindByIdAsync(command.ReturnId, cancellationToken);

        if (request is null)
            return new DecideReturnResult(DecideReturnOutcome.UnknownReturn, null, null);

        try
        {
            switch (command.Decision)
            {
                case ReturnDecision.Approve:
                    request.Approve(clock);
                    break;

                case ReturnDecision.Reject:
                    request.Reject(clock, command.Reason ?? "The shop declined this return.");
                    break;

                case ReturnDecision.Receive:
                    // Restocking hangs off the EVENT this raises, not off this
                    // line: the goods reach the shelf through the outbox by the
                    // same path an order's release takes.
                    request.Receive(clock);
                    break;

                case ReturnDecision.Refund:
                    var refunded = await Refund(request, cancellationToken);
                    if (refunded is not null)
                        return refunded;
                    break;
            }
        }
        catch (InvalidOperationException exception)
        {
            return new DecideReturnResult(DecideReturnOutcome.IllegalTransition, request, exception.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new DecideReturnResult(DecideReturnOutcome.Decided, request, null);
    }

    /// <summary>
    /// The money back, through the payment port.
    ///
    /// It refunds the CAPTURE and not the authorisation, and an order that was
    /// never captured has nothing to give back — which is a different sentence
    /// from "the refund failed", and a shopkeeper needs to be told which.
    /// </summary>
    private async Task<DecideReturnResult?> Refund(
        ReturnRequest request, CancellationToken cancellationToken)
    {
        var order = await orders.FindByIdAsync(request.OrderId, cancellationToken);

        if (order?.Payment is not { CaptureId: not null } payment)
            return new DecideReturnResult(
                DecideReturnOutcome.NothingToRefund, request,
                "This order was never captured, so there is nothing to refund.");

        var amount = request.RefundableFrom(order);

        var refund = await payments.Get(payment.Provider)
            .RefundAsync(payment.CaptureId!, amount, cancellationToken);

        if (!refund.Succeeded)
            return new DecideReturnResult(DecideReturnOutcome.RefundFailed, request, refund.FailureReason);

        request.Refund(clock, amount, refund.RefundId!);

        return null;
    }
}

// ---------- Restocking ----------

/// <summary>
/// The goods are back on the shelf.
///
/// It hangs off <c>ReturnReceived</c> and not off approval, because approving is
/// a promise and receiving is a fact — the same distinction that makes stock
/// commit on shipping rather than on confirming. A shop that restocked on
/// approval would be selling parcels that are still in the post.
///
/// It goes through <c>IStockLedger.ReceiveAsync</c>, so a returned pan reaches
/// the search index by exactly the path a delivery takes.
/// </summary>
public sealed class RestockOnReturnReceived(
    IReturnRequestRepository returns,
    IStockLedger stock,
    IOptions<ReturnOptions> options,
    ILogger<RestockOnReturnReceived> logger) : IDomainEventHandler<ReturnReceived>
{
    public async Task HandleAsync(ReturnReceived domainEvent, CancellationToken cancellationToken)
    {
        var request = await returns.FindByIdAsync(domainEvent.ReturnId, cancellationToken);

        if (request is null)
            return;

        foreach (var line in request.Lines)
        {
            // Damaged goods do not go back on sale. It is one line of code and
            // it is the difference between a returns loop and a way to sell
            // broken things twice.
            if (line.Reason == ReturnReason.Damaged)
            {
                logger.LogInformation(
                    "Return {ReturnId}: {Quantity}x {Sku} came back damaged and was not restocked",
                    request.Id, line.Quantity, line.Sku);
                continue;
            }

            await stock.ReceiveAsync(line.Sku, options.Value.RestockWarehouse, line.Quantity, cancellationToken);
        }
    }
}

public sealed class ReturnOptions
{
    public const string SectionName = "Returns";

    /// <summary>
    /// Where returned goods land. Configuration and not a rule: a shop with a
    /// dedicated returns warehouse changes a string, and one without does not
    /// have to model a concept it does not have.
    /// </summary>
    public string RestockWarehouse { get; set; } = "MAD";
}

// ---------- Reading ----------

public sealed record ReturnLineView(
    string Sku, int Quantity, string Reason, string? Comment);

public sealed record ReturnView(
    string ReturnId,
    string OrderId,
    string Status,
    string? Resolution,
    decimal? RefundAmount,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReturnLineView> Lines)
{
    public static ReturnView From(ReturnRequest request) => new(
        request.Id.ToString(),
        request.OrderId.ToString(),
        request.Status.ToString(),
        request.Resolution,
        request.RefundAmount?.Amount,
        request.CreatedAt,
        [
            .. request.Lines.Select(line => new ReturnLineView(
                line.Sku, line.Quantity, line.Reason.ToString(), line.Comment))
        ]);
}

public sealed record ListReturnsQuery : IQuery<IReadOnlyList<ReturnView>>;

public sealed class ListReturnsHandler(IReturnRequestRepository returns)
    : IQueryHandler<ListReturnsQuery, IReadOnlyList<ReturnView>>
{
    public async Task<IReadOnlyList<ReturnView>> HandleAsync(
        ListReturnsQuery query, CancellationToken cancellationToken) =>
        [.. (await returns.OpenAsync(cancellationToken)).Select(ReturnView.From)];
}

public sealed class ReturnEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/orders/{orderId:guid}/returns",
            async Task<Results<Ok<ReturnView>, BadRequest<string>, NotFound<string>, Conflict<string>>> (
                   Guid orderId, RequestReturnRequest request, string? culture,
                   HttpContext http, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var resolved = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(
                    new RequestReturnCommand(new OrderId(orderId), request.Lines, resolved), ct);

                http.Response.Headers.ContentLanguage = resolved;

                return result.Outcome switch
                {
                    RequestReturnOutcome.UnknownOrder => TypedResults.NotFound($"There is no order {orderId}."),
                    RequestReturnOutcome.WindowClosed => TypedResults.Conflict(result.Detail!),
                    RequestReturnOutcome.Refused => TypedResults.BadRequest(result.Detail!),
                    _ => TypedResults.Ok(ReturnView.From(result.Return!))
                };
            })
            // A shopper opens their own return, and until phase 7 there is no
            // account to check it against — the order id is the credential, and
            // it is a GUID v7 nobody can enumerate. Said in code rather than
            // omitted, which is what the endpoint-policy test enforces.
            .AllowAnonymous()
            .WithTags("Returns")
            .WithName("RequestReturn");

        var queue = app.MapGroup("/api/returns")
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Returns");

        queue.MapGet("/",
            async Task<Ok<IReadOnlyList<ReturnView>>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ListReturnsQuery(), ct)))
            .WithName("ListReturns");

        queue.MapPost("/{returnId:guid}/decide",
            async Task<Results<Ok<ReturnView>, BadRequest<string>, NotFound<string>, Conflict<string>>> (
                   Guid returnId, DecideReturnRequest request,
                   ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                if (!Enum.TryParse<ReturnDecision>(request.Decision, ignoreCase: true, out var decision))
                    return TypedResults.BadRequest(
                        $"A decision is one of {string.Join(", ", Enum.GetNames<ReturnDecision>())}.");

                var result = await dispatcher.SendAsync(
                    new DecideReturnCommand(new ReturnRequestId(returnId), decision, request.Reason), ct);

                return result.Outcome switch
                {
                    DecideReturnOutcome.UnknownReturn => TypedResults.NotFound($"There is no return {returnId}."),
                    DecideReturnOutcome.IllegalTransition => TypedResults.Conflict(result.Detail!),
                    DecideReturnOutcome.NothingToRefund => TypedResults.Conflict(result.Detail!),
                    DecideReturnOutcome.RefundFailed => TypedResults.BadRequest(result.Detail!),
                    _ => TypedResults.Ok(ReturnView.From(result.Return!))
                };
            })
            .WithName("DecideReturn");
    }
}
