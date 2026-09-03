using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Accounts.Features.LinkIdentity;

/// <summary>
/// Somebody signed in. This is where they become a customer.
///
/// **It is a command and not a side effect of authenticating**, and that is the
/// decision worth defending. The obvious alternative — have the principal
/// accessor create a customer the first time it sees an unknown `sub` — turns
/// every GET into a write: a page view would register an account, a retry would
/// have to be idempotent for a reason nobody could see from the endpoint, and
/// the row would appear with no audit entry because queries are not audited.
///
/// Calling it explicitly costs the storefront one request after login and makes
/// registration a thing that HAPPENED, with a row in the audit log saying so.
///
/// Idempotent, like everything here that a browser can send twice: signing in
/// again returns the same customer and writes nothing.
/// </summary>
public sealed record LinkIdentityCommand(
    string? DisplayName,
    string Culture,
    /// <summary>
    /// A guest the caller wants merged into this account.
    ///
    /// Optional, and usually absent. It is the caller's own guest id — the one
    /// its cart or its last order was placed under — and supplying somebody
    /// else's would be claiming their orders, which is why the handler refuses
    /// any id that is not a guest.
    /// </summary>
    CustomerId? Guest)
    : ICommand<LinkedIdentity>;

public sealed record LinkedIdentity(
    string CustomerId,
    string? DisplayName,
    string Segment,
    string Culture,
    LinkOutcome Outcome);

/// <summary>
/// What signing in did. Three cases, because they are three different facts and
/// the audit log should be able to tell them apart.
/// </summary>
public enum LinkOutcome
{
    /// <summary>The shop had never seen this subject.</summary>
    Registered,

    /// <summary>A guest this caller was already using became them.</summary>
    Linked,

    /// <summary>They already had an account. A guest, if one was offered, was
    /// merged into it.</summary>
    Recognised
}

public sealed class LinkIdentityValidator : AbstractValidator<LinkIdentityCommand>
{
    public LinkIdentityValidator()
    {
        RuleFor(command => command.DisplayName).MaximumLength(200);

        RuleFor(command => command.Culture).Must(culture => culture is "es" or "en")
            .WithMessage("Supported cultures: es, en.");
    }
}

public sealed class LinkIdentityEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/accounts/me
        //
        // The SUBJECT is never in the body. It comes from the validated token
        // and nowhere else — a body that could name a subject would let anybody
        // claim anybody's account, which is the whole of the vulnerability.
        app.MapPost("/api/accounts/me",
            async Task<Ok<LinkedIdentity>> (
                   LinkIdentityRequest? request,
                   HttpContext http, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                var culture = CultureNegotiation.Resolve(
                    request?.Culture, http.Request.Headers.AcceptLanguage);

                var result = await dispatcher.SendAsync(
                    new LinkIdentityCommand(
                        request?.DisplayName,
                        culture,
                        request?.Guest is { } guest ? new CustomerId(guest) : null),
                    ct);

                http.Response.Headers.ContentLanguage = culture;

                return TypedResults.Ok(result);
            })
            .RequireAuthorization()
            .WithTags("Accounts")
            .WithName("LinkIdentity");
    }
}

public sealed record LinkIdentityRequest(string? DisplayName, string? Culture, Guid? Guest);

public sealed class LinkIdentityHandler(
    ICustomerRepository customers,
    IPrincipalAccessor principals,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<LinkIdentityCommand, LinkedIdentity>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    public async Task<LinkedIdentity> HandleAsync(
        LinkIdentityCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("accounts.link_identity");

        var subject = principals.Current.Subject
            ?? throw new InvalidOperationException(
                "Linking an identity needs an authenticated subject.");

        activity?.SetTag("accounts.subject", subject);

        var existing = await customers.FindBySubjectAsync(subject, cancellationToken);

        if (existing is not null)
        {
            // They already had an account. A guest offered alongside is merged
            // into it — which is the case a person hits when they shopped as a
            // guest on a device where they later signed in.
            var merged = await MergeAsync(command.Guest, existing.Id, cancellationToken);

            existing.UseCulture(clock, command.Culture);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            activity?.SetTag("accounts.outcome", nameof(LinkOutcome.Recognised));
            activity?.SetTag("accounts.merged_guest", merged);

            return View(existing, LinkOutcome.Recognised);
        }

        // A guest of their own becomes them: the id their orders already carry
        // keeps working, which is the whole reason a guest is a Customer rather
        // than a null.
        if (command.Guest is { } guestId
            && await customers.FindByIdAsync(guestId, cancellationToken) is { IsGuest: true } guest)
        {
            guest.LinkIdentity(clock, subject, command.DisplayName);
            guest.UseCulture(clock, command.Culture);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            activity?.SetTag("accounts.outcome", nameof(LinkOutcome.Linked));

            return View(guest, LinkOutcome.Linked);
        }

        var registered = Customer.Identified(
            clock, subject, command.DisplayName, command.Culture, Segments.Retail);

        customers.Add(registered);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        activity?.SetTag("accounts.outcome", nameof(LinkOutcome.Registered));

        return View(registered, LinkOutcome.Registered);
    }

    /// <summary>
    /// Points a guest at the account that survives.
    ///
    /// It refuses anything that is not a guest, and the refusal is the security
    /// boundary rather than a tidiness check: the guest id arrives from the
    /// caller, so a handler that merged any id it was given would let anybody
    /// absorb anybody else's account by guessing a GUID.
    /// </summary>
    private async Task<bool> MergeAsync(
        CustomerId? guestId, CustomerId survivor, CancellationToken cancellationToken)
    {
        if (guestId is not { } id || id == survivor)
            return false;

        var guest = await customers.FindByIdAsync(id, cancellationToken);

        if (guest is null || !guest.IsGuest || guest.SupersededBy is not null)
            return false;

        guest.Supersede(clock, survivor);
        return true;
    }

    private static LinkedIdentity View(Customer customer, LinkOutcome outcome) =>
        // A flat string, never the record struct: `CustomerId` serialises as
        // {"value":"…"} and the wire wants an id (CLAUDE.md).
        new(customer.Id.Value.ToString(),
            customer.DisplayName,
            customer.Segment,
            customer.Culture,
            outcome);
}
