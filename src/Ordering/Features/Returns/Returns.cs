using System.Diagnostics;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Inventory.Ports;
using ElGuerre.Tendero.Ordering.Contracts;
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
    RequestReturnOutcome Outcome, ReturnRequest? Return, string? Detail) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        RequestReturnOutcome.Opened => AuditOutcome.Allowed,
        RequestReturnOutcome.UnknownOrder or RequestReturnOutcome.WindowClosed or
            RequestReturnOutcome.Refused => AuditOutcome.Denied
    };

    string? IAuditedResult.AuditReason =>
        Outcome == RequestReturnOutcome.Opened ? null : Detail ?? Outcome.ToString();
}

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
    TimeProvider clock,
    OrderAccess access)
    : ICommandHandler<RequestReturnCommand, RequestReturnResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Ordering);

    public async Task<RequestReturnResult> HandleAsync(
        RequestReturnCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("returns.request");
        activity?.SetTag("ordering.order_id", command.OrderId.Value);

        var order = await orders.FindByIdAsync(command.OrderId, cancellationToken);

        // An account holder's order is theirs to return, and to a stranger it
        // does not exist — the same answer the order page gives (OrderAccess).
        if (order is null || !await access.CanReadAsync(order, cancellationToken))
            return new RequestReturnResult(RequestReturnOutcome.UnknownOrder, null, null);

        if (!ReturnRequest.WindowIsOpen(order, clock.GetUtcNow()))
            return new RequestReturnResult(
                RequestReturnOutcome.WindowClosed, null,
                order.Status == OrderStatus.Delivered
                    ? $"The {ReturnRequest.Window.TotalDays:F0}-day return window has closed."
                    : $"This order is {order.Status} and has not been delivered yet.");

        // Every return already opened against this order: what a shopper may
        // still send back depends on them, and the aggregate refuses to guess.
        var earlier = await returns.ForOrderAsync(order.Id, cancellationToken);

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
            ], earlier);

            returns.Add(request);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            ReturnedLines.Record(request);

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
        catch (ConcurrentWriteException)
        {
            // Another return of this order was opened between reading the earlier
            // ones and saving this one, and the unique (order, number) index let
            // that one in (ReturnRequest.Number). What is left to return has
            // changed, so the only honest answer is to look again.
            return new RequestReturnResult(
                RequestReturnOutcome.Refused, null,
                "Another return for this order was opened at the same moment. Reload the order and try again.");
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
    DecideReturnOutcome Outcome, ReturnRequest? Return, string? Detail) : IAuditedResult
{
    /// <summary>A refund the provider refused is a fault; everything else that
    /// is not a decision is a rule saying no.</summary>
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        DecideReturnOutcome.Decided => AuditOutcome.Allowed,
        DecideReturnOutcome.RefundFailed => AuditOutcome.Failed,
        DecideReturnOutcome.UnknownReturn or DecideReturnOutcome.IllegalTransition or
            DecideReturnOutcome.NothingToRefund => AuditOutcome.Denied
    };

    string? IAuditedResult.AuditReason =>
        Outcome == DecideReturnOutcome.Decided ? null : Detail ?? Outcome.ToString();
}

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

public sealed record ListReturnsQuery : IQuery<IReadOnlyList<ReturnView>>;

public sealed class ListReturnsHandler(IReturnRequestRepository returns)
    : IQueryHandler<ListReturnsQuery, IReadOnlyList<ReturnView>>
{
    public async Task<IReadOnlyList<ReturnView>> HandleAsync(
        ListReturnsQuery query, CancellationToken cancellationToken) =>
        [.. (await returns.OpenAsync(cancellationToken)).Select(ReturnView.From)];
}

/// <summary>
/// What the shop promises before anybody has bought anything.
///
/// The window is enforced in one place — <see cref="ReturnRequest.CanOpenAt"/>
/// measures it from delivery — and said out loud in two: the product page, which
/// is where roughly sixty per cent of shoppers look for it and where forty-four
/// per cent of shops do not put it, and the order page afterwards. Publishing it
/// is what stops those two from becoming copies: the order page used to write
/// the number into its own template, and shortening the window would have left
/// it promising the old one with nothing red anywhere.
///
/// Same decision as <c>/api/auth/config</c>, one value to the left.
/// </summary>
public sealed record ReturnPolicy(
    /// <summary>Days from DELIVERY, not from purchase — which is the half a
    /// promise usually gets wrong.</summary>
    int WindowDays);

/// <summary>
/// The promise, through the dispatcher.
///
/// This carried no query until phase 9, and the comment on the endpoint said
/// why: a round trip around a field read is ceremony. That was true while there
/// was one caller. There are two transports now — HTTP and the MCP server — and
/// MCP may not touch a <c>*.Domain</c> namespace, so the alternative is not "no
/// ceremony", it is the same constant read in two places with nothing comparing
/// them. Which is the defect this endpoint was built to remove, one level up.
/// </summary>
public sealed record ReturnPolicyQuery : IQuery<ReturnPolicy>;

public sealed class ReturnPolicyHandler : IQueryHandler<ReturnPolicyQuery, ReturnPolicy>
{
    public Task<ReturnPolicy> HandleAsync(ReturnPolicyQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new ReturnPolicy((int)ReturnRequest.Window.TotalDays));
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
            // Anonymous at the door because a guest has no account to show: for
            // a guest's order the id is the credential, a GUID v7 nobody can
            // enumerate. An account holder's order is checked inside, by
            // OrderAccess — which is where "whose order is this" can be asked.
            .AllowAnonymous()
            .WithTags("Returns")
            .WithName("RequestReturn");

        // Outside the group below on purpose: that one requires the
        // shopkeeper, and this is read by somebody who has not signed in and may
        // never sign in. Anonymous by an explicit decision, which is what the
        // endpoint-policy test asks for.
        //
        // It went through the dispatcher in phase 9, and the comment that used to
        // sit here said the opposite: no input to validate, no data to read,
        // nothing to audit, so a round trip would be ceremony around a field
        // read. That held while HTTP was the only way in. The MCP server is a
        // second transport and may not reach a `*.Domain` namespace, so the
        // choice stopped being "one line or a round trip" and became "one source
        // or two copies of a constant" — and this endpoint exists precisely
        // because the window was written down twice.
        app.MapGet("/api/returns/policy",
            async Task<Ok<ReturnPolicy>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ReturnPolicyQuery(), ct)))
            .AllowAnonymous()
            .WithTags("Returns")
            .WithName("ReturnPolicy");

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

/// <summary>
/// `tendero.returns.requested` (the tracking plan): each line of an opened
/// return, by its reason. The five reasons are a closed set for exactly this
/// analysis — wrong size and not as described are the product reasoning layer's
/// case, damaged and wrong item the warehouse's.
/// </summary>
internal static class ReturnedLines
{
    private static readonly Meter Meter = new(TelemetrySources.Ordering);

    private static readonly Counter<long> Requested = Meter.CreateCounter<long>(
        "tendero.returns.requested", unit: "{line}", description: "Lines of the returns opened, by their reason.");

    public static void Record(ReturnRequest request)
    {
        foreach (var line in request.Lines)
            Requested.Add(1, new KeyValuePair<string, object?>("reason", MetricLabels.Of(line.Reason)));
    }
}
