using System.Diagnostics;
using System.Diagnostics.Metrics;
using Carter;
using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Accounts.Features.ImportCustomers;

/// <param name="Culture">The language the notes are said in, picked as every endpoint picks it (ADR 0013).</param>
/// <param name="Rehearse">Read the source and answer what would come, with nothing written and nobody provisioned (phase 16, P16-13's rule).</param>
public sealed record ImportCustomersCommand(string Source, string Culture = "es", bool Rehearse = false) : ICommand<ImportCustomersResult>;

/// <param name="Created">Customers made, each with an identity when one could be provisioned.</param>
/// <param name="AlreadyImported">Customers a previous import brought, found by the store's id and left as they are.</param>
/// <param name="Linked">People the shop already had, linked to the store's id through their identity.</param>
public sealed record ImportCustomersResult(
    int Created, int AlreadyImported, int Linked, IReadOnlyList<Message<CustomerNoteCode>> Notes);

public sealed class ImportCustomersValidator : AbstractValidator<ImportCustomersCommand>
{
    /// <summary>The source must exist, not just look like one: the catalogue's lesson.</summary>
    public ImportCustomersValidator(ICustomerSourceRegistry sources)
    {
        RuleFor(command => command.Source)
            .NotEmpty()
            .Must(source => sources.Sources.Contains(source, StringComparer.Ordinal))
            .WithMessage(command =>
                $"Unknown customer source '{command.Source}'. Registered sources: {string.Join(", ", sources.Sources)}.");
    }
}

public sealed class ImportCustomersEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/accounts/import  { "source": "shopify" }, or with "rehearse": true
        app.MapPost("/api/accounts/import",
                async Task<Ok<ImportCustomersResult>> (
                    ImportCustomersCommand command, string? culture, HttpContext http,
                    ICommandDispatcher dispatcher, CancellationToken ct) =>
                {
                    var language = CultureNegotiation.Resolve(culture, http.Request.Headers.AcceptLanguage);
                    var result = await dispatcher.SendAsync(command with { Culture = language }, ct);
                    http.Response.Headers.ContentLanguage = language;
                    http.Response.Headers.Vary = "Accept-Language";
                    return TypedResults.Ok(result);
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Accounts")
            .WithName("ImportCustomers");
    }
}

/// <summary>
/// A store's customers into the shop (phase 16, S2b, P16-17; spec decision S2b-1,
/// A). Each comes with an identity provisioned for them — no password: the
/// provider's *forgot password* sets one and proves the address is theirs — their
/// addresses in their book, and their marketing consent as the store dated it,
/// worded as what it was: a consent given to the store.
///
/// Imported once. A re-import finds each by the store's id; a person the shop
/// already had is found through their identity (the provider answers with the one
/// that had the email) and linked, not made again; somebody erased here asked to
/// be forgotten, and stays forgotten. No email is sent to anybody (decision S2b-3).
/// </summary>
public sealed class ImportCustomersHandler(
    ICustomerSourceRegistry sources,
    ICustomerRepository customers,
    IImportedCustomers imported,
    IAddressBookRepository books,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IEnumerable<IIdentityProvisioner> provisioners,
    IRehearsedCustomers rehearsed)
    : ICommandHandler<ImportCustomersCommand, ImportCustomersResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    public async Task<ImportCustomersResult> HandleAsync(ImportCustomersCommand command, CancellationToken cancellationToken)
    {
        var source = sources.Get(command.Source);
        var provisioner = provisioners.FirstOrDefault();

        using var activity = Telemetry.StartActivity("accounts.import");
        activity?.SetTag("catalog.source", source.Source);
        activity?.SetTag("accounts.import.rehearsal", command.Rehearse);

        var notes = new List<CustomerNote>();
        if (provisioner is null)
            notes.Add(new CustomerNote(CustomerNoteCode.IdentityNotProvisioned, null, null));

        int created = 0, alreadyImported = 0, linked = 0;

        await foreach (var person in source.StreamCustomersAsync(cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(person.Email))
            {
                notes.Add(new CustomerNote(CustomerNoteCode.NoEmail, person.ExternalId, null));
                continue;
            }

            var origin = new CustomerOrigin(source.Source, person.ExternalId);
            if (await imported.FindByOriginAsync(origin, cancellationToken) is { } known)
            {
                if (known.IsErased)
                    notes.Add(new CustomerNote(CustomerNoteCode.Erased, person.ExternalId, null));
                else
                    alreadyImported++;
                continue;
            }

            // A rehearsal provisions nobody: it counts who would come, and says why not, and
            // holds them for an order rehearsed in the same request to find (P16-21).
            if (command.Rehearse)
            {
                rehearsed.Bring(origin);
                created++;
                Addresses(person, notes);
                continue;
            }

            var subject = await ProvisionAsync(provisioner, person, notes, cancellationToken);
            if (subject is not null && await customers.FindBySubjectAsync(subject, cancellationToken) is { } existing)
            {
                existing.LinkOrigin(clock, origin);
                notes.Add(new CustomerNote(CustomerNoteCode.AlreadyHere, person.ExternalId, person.Email));
                linked++;
                continue;
            }

            var customer = Customer.Imported(
                clock, origin, person.Email, NameOf(person), person.Culture ?? "es", ConsentOf(person, source.Source), subject);
            customers.Add(customer);
            KeepAddresses(customer, Addresses(person, notes));
            created++;
        }

        if (!command.Rehearse && (created > 0 || linked > 0))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        notes.AddRange(source.SourceNotes);
        if (!command.Rehearse)
            ImportCustomersMetrics.Record(source.Source, created, alreadyImported, linked, notes);

        var english = command.Culture == "en";
        return new ImportCustomersResult(created, alreadyImported, linked, [.. notes.Select(note => ImportCustomersNotes.Said(note, english))]);
    }

    /// <summary>The identity for them, or null — with a note — when there is no provisioner or it refused.</summary>
    private static async Task<string?> ProvisionAsync(
        IIdentityProvisioner? provisioner, SourceCustomer person, List<CustomerNote> notes, CancellationToken cancellationToken)
    {
        if (provisioner is null)
            return null;

        try
        {
            return await provisioner.ProvisionAsync(
                new IdentityToProvision(person.Email!.Trim().ToLowerInvariant(), person.FirstName, person.LastName, person.Culture),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            notes.Add(new CustomerNote(CustomerNoteCode.IdentityNotProvisioned, person.ExternalId, exception.Message));
            return null;
        }
    }

    private static string? NameOf(SourceCustomer person)
    {
        var name = string.Join(' ', new[] { person.FirstName, person.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// The store's consent, dated when the store recorded it. Its words are not ours
    /// to have, so the wording is the version a consent is kept against, naming what
    /// it was: <c>imported:shopify</c>, a consent given to that store.
    /// </summary>
    private static MarketingConsent? ConsentOf(SourceCustomer person, string source) =>
        person.Marketing is { } consent ? new MarketingConsent(consent.Given, consent.At, $"imported:{source}") : null;

    /// <summary>The addresses the shop can read, each noted when it cannot; the store's default first.</summary>
    private static List<(Address Address, bool IsDefault)> Addresses(SourceCustomer person, List<CustomerNote> notes)
    {
        var readable = new List<(Address, bool)>();
        foreach (var address in person.Addresses.Take(AddressBook.MaxEntries))
        {
            try
            {
                readable.Add((Address.Create(address.Recipient, address.Line1, address.Line2, address.City, address.Region,
                    address.PostalCode, address.CountryCode, address.Phone), address.IsDefault));
            }
            catch (ArgumentException)
            {
                notes.Add(new CustomerNote(CustomerNoteCode.AddressUnreadable, person.ExternalId, address.City));
            }
        }

        return readable;
    }

    private void KeepAddresses(Customer customer, List<(Address Address, bool IsDefault)> addresses)
    {
        if (addresses.Count == 0)
            return;

        var book = AddressBook.StartFor(customer.Id, clock);
        foreach (var (address, isDefault) in addresses)
        {
            var label = address.City.Length > AddressBook.MaxLabelLength ? address.City[..AddressBook.MaxLabelLength] : address.City;
            book.Add(clock, label, address, delivery: isDefault, billing: isDefault);
        }

        books.Add(book);
    }
}

/// <summary>A customer note as the answer says it: the code, what it is about, and the sentence in the language asked.</summary>
internal static class ImportCustomersNotes
{
    public static Message<CustomerNoteCode> Said(CustomerNote note, bool english) =>
        new(note.Code, note.About, Sentences.StartingUpper(Sentence(note, english)));

    private static string Sentence(CustomerNote note, bool english)
    {
        var about = note.About is null ? "" : $"{note.About}: ";
        var detail = note.Detail ?? "";

        return note.Code switch
        {
            CustomerNoteCode.FieldRedacted => english
                ? $"{about}the store withheld {detail}, a field the app was not granted; the customer came without it."
                : $"{about}la tienda no ha dado {detail}, un campo que la app no tiene concedido; el cliente ha venido sin él.",
            CustomerNoteCode.NoEmail => english
                ? $"{about}a customer without an email cannot sign in or be written to; they were not imported."
                : $"{about}un cliente sin email no puede entrar ni recibir nada; no se ha importado.",
            CustomerNoteCode.AlreadyHere => english
                ? $"{about}{detail} was already a customer here; linked to the store, not imported twice."
                : $"{about}{detail} ya era cliente aquí; se ha enlazado con la tienda y no se ha importado dos veces.",
            CustomerNoteCode.IdentityNotProvisioned => english
                ? $"{about}no identity could be made for them{(detail.Length > 0 ? $" ({detail})" : "")}; they came as a guest the shop can still address."
                : $"{about}no se ha podido crear su identidad{(detail.Length > 0 ? $" ({detail})" : "")}; ha venido como invitado al que la tienda puede seguir atendiendo.",
            CustomerNoteCode.AddressUnreadable => english
                ? $"{about}an address in {detail} could not be read; the customer came without it."
                : $"{about}no se ha podido leer una dirección de {detail}; el cliente ha venido sin ella.",
            CustomerNoteCode.Erased => english
                ? $"{about}this customer asked to be forgotten here; the import does not bring them back."
                : $"{about}este cliente pidió aquí que se le olvidara; la importación no lo trae de vuelta.",
        };
    }
}

/// <summary>
/// `tendero.accounts.import.customers` and `tendero.accounts.import.notes` (the
/// tracking plan; phase 16, spec *Evidence of use*): how many of a store's people
/// come across, by source, labelled as the catalogue's and the stock's imports are.
/// </summary>
internal static class ImportCustomersMetrics
{
    private static readonly Meter Meter = new(TelemetrySources.Accounts);

    private static readonly Counter<long> Customers = Meter.CreateCounter<long>(
        "tendero.accounts.import.customers", unit: "{customer}", description: "Customers an import created, found already imported or linked, by source.");

    private static readonly Counter<long> Notes = Meter.CreateCounter<long>(
        "tendero.accounts.import.notes", unit: "{note}", description: "What a customer source did not hand over cleanly, by source and code.");

    public static void Record(string source, int created, int alreadyImported, int linked, IEnumerable<CustomerNote> notes)
    {
        Add(source, "created", created);
        Add(source, "already_imported", alreadyImported);
        Add(source, "linked", linked);

        foreach (var group in notes.GroupBy(note => note.Code))
        {
            Notes.Add(
                group.Count(),
                new KeyValuePair<string, object?>("catalog.source", source),
                new KeyValuePair<string, object?>("accounts.import.note", group.Key.ToString()));
        }
    }

    private static void Add(string source, string outcome, int count)
    {
        if (count == 0)
            return;

        Customers.Add(
            count,
            new KeyValuePair<string, object?>("catalog.source", source),
            new KeyValuePair<string, object?>("accounts.import.outcome", outcome));
    }
}
