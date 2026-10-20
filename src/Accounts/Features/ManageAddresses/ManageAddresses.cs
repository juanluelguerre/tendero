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

namespace ElGuerre.Tendero.Accounts.Features.ManageAddresses;

// A signed-in customer's address book (P15-2, docs/specs/shopper-account.md):
// read, add, edit, remove, and choose the default for delivery and for billing.
// The rules are the aggregate's (AddressBook); this slice fetches, applies and
// answers.
//
// **The customer is the caller, always**, as the trail's slice says: no route or
// body names a customer. And a person, never an agent — the in-browser agent
// acts inside the shopper's session and sees what the page shows; what a
// server-to-server agent may use of the book is decision 8, and it is nothing yet.
//
// **Audited with the address redacted.** The pipeline writes a row per command;
// the row says an address was kept, changed or removed and when, never where
// nor what the shopper calls it — a label is their words, and can be a name.

// ---------- The wire ----------

/// <summary>An address as the book keeps it: the kernel's fields, flat.</summary>
public sealed record SavedAddress(
    string RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string CountryCode,
    string? Phone)
{
    public static SavedAddress Of(Address address) => new(
        address.RecipientName, address.Line1, address.Line2, address.City,
        address.Region, address.PostalCode, address.CountryCode, address.Phone);
}

/// <param name="Id">The entry's identity, flat, as the routes take it.</param>
/// <param name="Label">The shopper's own name for it, in their words.</param>
public sealed record AddressEntryView(string Id, string Label, SavedAddress Address, bool DeliveryDefault, bool BillingDefault);

/// <param name="Entries">Oldest first, at most <paramref name="MaxEntries"/>.</param>
/// <param name="MaxEntries">How many the book keeps, so the page can say *3 of 10* without knowing the rule.</param>
public sealed record AddressBookView(IReadOnlyList<AddressEntryView> Entries, int MaxEntries)
{
    public static readonly AddressBookView Empty = new([], AddressBook.MaxEntries);

    public static AddressBookView Of(AddressBook? book) => book is null
        ? Empty
        : new(
            [.. book.Entries.Select(entry => new AddressEntryView(
                entry.Id.ToString(), entry.Label, SavedAddress.Of(entry.Address),
                book.DeliveryDefault == entry.Id, book.BillingDefault == entry.Id))],
            AddressBook.MaxEntries);
}

/// <summary>An entry as the form sends it, to add or to replace one.</summary>
public sealed record AddressEntryRequest(string? Label, SavedAddress? Address, bool DeliveryDefault, bool BillingDefault);

// ---------- Commands and the query ----------

public sealed record GetAddressBookQuery : IQuery<AddressBookView>;

public sealed record AddAddressCommand(
    [property: AuditRedacted] string Label,
    [property: AuditRedacted] SavedAddress Address,
    bool DeliveryDefault,
    bool BillingDefault) : ICommand<AddressBookResult>;

public sealed record EditAddressCommand(
    AddressEntryId Id,
    [property: AuditRedacted] string Label,
    [property: AuditRedacted] SavedAddress Address,
    bool DeliveryDefault,
    bool BillingDefault) : ICommand<AddressBookResult>;

public sealed record RemoveAddressCommand(AddressEntryId Id) : ICommand<AddressBookResult>;

public sealed record UseAddressCommand(AddressEntryId Id, AddressUse Use) : ICommand<AddressBookResult>;

/// <summary>What a change to the book came to. A closed set: each endpoint names every case.</summary>
public enum AddressBookOutcome
{
    Done,
    /// <summary>Signed in, and the identity not yet linked to a customer: nobody to keep an address for.</summary>
    NoCustomer,
    Full,
    UnknownEntry,
    InvalidAddress,
}

/// <param name="Reason">The domain's English sentence, when the address was refused: for a log, never a screen.</param>
public sealed record AddressBookResult(AddressBookOutcome Outcome, AddressBookView View, string? Reason = null) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        AddressBookOutcome.Done => AuditOutcome.Allowed,
        AddressBookOutcome.NoCustomer or AddressBookOutcome.Full
            or AddressBookOutcome.UnknownEntry or AddressBookOutcome.InvalidAddress => AuditOutcome.Denied,
    };

    // The outcome's name and never the domain's sentence: that sentence can quote
    // the address it refused, and the audit row says where nothing is.
    string? IAuditedResult.AuditReason => Outcome == AddressBookOutcome.Done ? null : Outcome.ToString();
}

// ---------- Refusals ----------

public static class AddressBookRefusals
{
    public static readonly Refusal NoCustomer = new(
        "NoCustomer", 409,
        "Tu cuenta todavía no está lista. Vuelve a entrar.", "Your account is not ready yet. Sign in again.");

    public static readonly Refusal Full = new(
        "AddressBookFull", 409,
        $"Ya tienes {AddressBook.MaxEntries} direcciones. Quita una para guardar otra.",
        $"You already have {AddressBook.MaxEntries} addresses. Remove one to keep another.");

    public static readonly Refusal UnknownEntry = new(
        "UnknownAddress", 404, "Esa dirección no está en tu libreta.", "That address is not in your address book.");

    public static readonly Refusal InvalidAddress = new(
        "InvalidAddress", 400, "La dirección no está completa o no es válida.", "The address is incomplete or not valid.");
}

// ---------- Validation ----------

/// <summary>What one entry may carry — one place for the add and the edit.</summary>
public static class AddressEntryRules
{
    /// <summary>Longer than any street the shop delivers to, short enough that a field cannot be used to store anything else.</summary>
    public const int MaxFieldLength = 200;

    public static void Apply<T>(AbstractValidator<T> validator, Func<T, string> label, Func<T, SavedAddress> address)
    {
        validator.RuleFor(command => label(command)).NotEmpty().MaximumLength(AddressBook.MaxLabelLength).OverridePropertyName("label");
        validator.RuleFor(command => address(command)).NotNull().OverridePropertyName("address");
        validator.RuleFor(command => address(command)).ChildRules(fields =>
        {
            fields.RuleFor(a => a.RecipientName).NotEmpty().MaximumLength(MaxFieldLength);
            fields.RuleFor(a => a.Line1).NotEmpty().MaximumLength(MaxFieldLength);
            fields.RuleFor(a => a.Line2).MaximumLength(MaxFieldLength);
            fields.RuleFor(a => a.City).NotEmpty().MaximumLength(MaxFieldLength);
            fields.RuleFor(a => a.Region).MaximumLength(MaxFieldLength);
            fields.RuleFor(a => a.PostalCode).NotEmpty().MaximumLength(20);
            fields.RuleFor(a => a.CountryCode).NotEmpty().Length(2);
            fields.RuleFor(a => a.Phone).MaximumLength(40);
        }).When(command => address(command) is not null).OverridePropertyName("address");
    }
}

public sealed class AddAddressValidator : AbstractValidator<AddAddressCommand>
{
    public AddAddressValidator() => AddressEntryRules.Apply(this, command => command.Label, command => command.Address);
}

public sealed class EditAddressValidator : AbstractValidator<EditAddressCommand>
{
    public EditAddressValidator() => AddressEntryRules.Apply(this, command => command.Label, command => command.Address);
}

public sealed class UseAddressValidator : AbstractValidator<UseAddressCommand>
{
    public UseAddressValidator() => RuleFor(command => command.Use).IsInEnum();
}

// ---------- Endpoints ----------

public sealed class ManageAddressesEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var addresses = app.MapGroup("/api/accounts/me/addresses")
            // A person's own addresses, read and changed only by their own session.
            .RequireAuthorization(TenderoPolicyNames.Shopper)
            .WithTags("Accounts");

        addresses.MapGet("",
            async Task<Ok<AddressBookView>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetAddressBookQuery(), ct)))
            .WithName("GetAddressBook");

        addresses.MapPost("",
            async Task<Results<Ok<AddressBookView>, ProblemHttpResult>> (AddressEntryRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Answer(await dispatcher.SendAsync(
                    new AddAddressCommand(request.Label ?? "", request.Address!, request.DeliveryDefault, request.BillingDefault), ct)))
            .WithName("AddAddress")
            .WithMetadata(new Refuses(AddressBookRefusals.NoCustomer, AddressBookRefusals.Full, AddressBookRefusals.InvalidAddress));

        addresses.MapPut("/{id:guid}",
            async Task<Results<Ok<AddressBookView>, ProblemHttpResult>> (Guid id, AddressEntryRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Answer(await dispatcher.SendAsync(
                    new EditAddressCommand(new AddressEntryId(id), request.Label ?? "", request.Address!, request.DeliveryDefault, request.BillingDefault), ct)))
            .WithName("EditAddress")
            .WithMetadata(new Refuses(AddressBookRefusals.NoCustomer, AddressBookRefusals.UnknownEntry, AddressBookRefusals.InvalidAddress));

        addresses.MapDelete("/{id:guid}",
            async Task<Results<Ok<AddressBookView>, ProblemHttpResult>> (Guid id, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Answer(await dispatcher.SendAsync(new RemoveAddressCommand(new AddressEntryId(id)), ct)))
            .WithName("RemoveAddress")
            .WithMetadata(new Refuses(AddressBookRefusals.NoCustomer, AddressBookRefusals.UnknownEntry));

        // PUT /api/accounts/me/addresses/{id}/default/Delivery
        addresses.MapPut("/{id:guid}/default/{use}",
            async Task<Results<Ok<AddressBookView>, ProblemHttpResult>> (Guid id, WireEnum<AddressUse> use, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Answer(await dispatcher.SendAsync(new UseAddressCommand(new AddressEntryId(id), use.Value), ct)))
            .WithName("UseAddressByDefault")
            .WithMetadata(new Refuses(AddressBookRefusals.NoCustomer, AddressBookRefusals.UnknownEntry));
    }

    private static Results<Ok<AddressBookView>, ProblemHttpResult> Answer(AddressBookResult result) => result.Outcome switch
    {
        AddressBookOutcome.Done => TypedResults.Ok(result.View),
        AddressBookOutcome.NoCustomer => Problem(AddressBookRefusals.NoCustomer),
        AddressBookOutcome.Full => Problem(AddressBookRefusals.Full),
        AddressBookOutcome.UnknownEntry => Problem(AddressBookRefusals.UnknownEntry),
        AddressBookOutcome.InvalidAddress => Problem(AddressBookRefusals.InvalidAddress, path: "$.address", reason: result.Reason),
    };

    private static ProblemHttpResult Problem(Refusal refusal, string? path = null, string? reason = null) =>
        TypedResults.Problem(statusCode: refusal.Status, extensions: refusal.Said(path: path, reason: reason));
}

// ---------- Handlers ----------

public sealed class GetAddressBookHandler(IAddressBookReader books, IPrincipalAccessor principals)
    : IQueryHandler<GetAddressBookQuery, AddressBookView>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    public async Task<AddressBookView> HandleAsync(GetAddressBookQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("accounts.addresses.read");

        // Signed in and not yet linked: nobody to have addresses. Empty rather than
        // refused, as the trail and `MyOrders` answer.
        if (principals.Current.Customer is not { } customer)
        {
            activity?.SetTag("accounts.unlinked", true);
            return AddressBookView.Empty;
        }

        var book = await books.ForCustomerAsync(customer, cancellationToken);
        activity?.SetTag("accounts.addresses.entries", book?.Entries.Count ?? 0);

        return AddressBookView.Of(book);
    }
}

public sealed class ManageAddressesHandlers(
    IAddressBookRepository books,
    IPrincipalAccessor principals,
    IUnitOfWork unitOfWork,
    TimeProvider clock) :
    ICommandHandler<AddAddressCommand, AddressBookResult>,
    ICommandHandler<EditAddressCommand, AddressBookResult>,
    ICommandHandler<RemoveAddressCommand, AddressBookResult>,
    ICommandHandler<UseAddressCommand, AddressBookResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    public Task<AddressBookResult> HandleAsync(AddAddressCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.addresses.add", create: true, book =>
        {
            if (book.IsFull)
                return (AddressBookOutcome.Full, null);

            if (!TryRead(command.Address, out var address, out var reason))
                return (AddressBookOutcome.InvalidAddress, reason);

            book.Add(clock, command.Label, address, command.DeliveryDefault, command.BillingDefault);
            return (AddressBookOutcome.Done, null);
        }, cancellationToken);

    public Task<AddressBookResult> HandleAsync(EditAddressCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.addresses.edit", create: false, book =>
        {
            if (!TryRead(command.Address, out var address, out var reason))
                return (AddressBookOutcome.InvalidAddress, reason);

            return book.Edit(clock, command.Id, command.Label, address, command.DeliveryDefault, command.BillingDefault)
                ? (AddressBookOutcome.Done, null)
                : (AddressBookOutcome.UnknownEntry, null);
        }, cancellationToken);

    public Task<AddressBookResult> HandleAsync(RemoveAddressCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.addresses.remove", create: false, book =>
            book.Remove(clock, command.Id) ? (AddressBookOutcome.Done, null) : (AddressBookOutcome.UnknownEntry, null),
            cancellationToken);

    public Task<AddressBookResult> HandleAsync(UseAddressCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.addresses.default", create: false, book =>
            book.UseFor(clock, command.Id, command.Use) ? (AddressBookOutcome.Done, null) : (AddressBookOutcome.UnknownEntry, null),
            cancellationToken);

    /// <summary>
    /// Load the caller's book — starting one when <paramref name="create"/> says
    /// the change needs somewhere to go — apply the change, save only when it was
    /// applied, and answer the book as it is now. Never an address on the span: a
    /// trace is exported further than a table.
    /// </summary>
    private async Task<AddressBookResult> ChangeAsync(
        string span, bool create, Func<AddressBook, (AddressBookOutcome Outcome, string? Reason)> change, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity(span);

        if (principals.Current.Customer is not { } customer)
        {
            activity?.SetTag("accounts.unlinked", true);
            return new AddressBookResult(AddressBookOutcome.NoCustomer, AddressBookView.Empty);
        }

        var book = await books.FindAsync(customer, cancellationToken);
        var started = false;

        if (book is null)
        {
            // Nothing to edit, remove or choose in a book that was never started.
            if (!create)
                return new AddressBookResult(AddressBookOutcome.UnknownEntry, AddressBookView.Empty);

            book = AddressBook.StartFor(customer, clock);
            started = true;
        }

        var (outcome, reason) = change(book);
        activity?.SetTag("accounts.addresses.outcome", outcome.ToString());

        if (outcome == AddressBookOutcome.Done)
        {
            if (started)
                books.Add(book);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        activity?.SetTag("accounts.addresses.entries", book.Entries.Count);
        return new AddressBookResult(outcome, AddressBookView.Of(started && outcome != AddressBookOutcome.Done ? null : book), reason);
    }

    /// <summary>The address's own validation lives on the value object, so it is the same wherever one is built; here it becomes an outcome.</summary>
    private static bool TryRead(SavedAddress fields, out Address address, out string? reason)
    {
        try
        {
            address = Address.Create(
                fields.RecipientName, fields.Line1, fields.Line2, fields.City,
                fields.Region, fields.PostalCode, fields.CountryCode, fields.Phone);
            reason = null;
            return true;
        }
        catch (ArgumentException exception)
        {
            address = null!;
            reason = exception.Message;
            return false;
        }
    }
}
