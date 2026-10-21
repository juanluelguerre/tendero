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

namespace ElGuerre.Tendero.Accounts.Features.ManagePreferences;

// A signed-in customer's preferences (P15-6, docs/specs/shopper-account.md,
// decision 7): the language the shop speaks to them in when a request names no
// other, and one marketing consent with its date and wording. No currency — the
// shop sells in one — and no analytics consent, which stays in the browser.
//
// **Nothing is sent yet.** There is no mailer; the consent is recorded for when
// there is, and the page says exactly that.

// ---------- The wire ----------

/// <param name="Given">True when given and not withdrawn.</param>
/// <param name="At">When the shopper last said, either way.</param>
/// <param name="Wording">The version of the wording they said it to.</param>
public sealed record MarketingConsentView(bool Given, DateTimeOffset At, string Wording);

/// <param name="Culture">The language the shop speaks to them in when a request names no other.</param>
/// <param name="CultureChosen">Whether they chose it here; until they do, it is the language last seen at sign-in.</param>
/// <param name="Marketing">Null until the shopper says: off until given.</param>
/// <param name="MarketingWording">The wording's current version, which a consent given now is given to.</param>
public sealed record PreferencesView(string Culture, bool CultureChosen, MarketingConsentView? Marketing, string MarketingWording)
{
    public static PreferencesView Of(Customer customer) => new(
        customer.Culture,
        customer.CultureChosen,
        customer.Marketing is { } consent ? new MarketingConsentView(consent.Given, consent.At, consent.Wording) : null,
        MarketingWordings.Current);
}

public sealed record CulturePreferenceRequest(string? Culture);

public sealed record MarketingPreferenceRequest(bool Given);

/// <summary>
/// The marketing consent's wording, versioned: the sentence the page shows is the
/// translation of this version, and changing the sentence means a new version — a
/// consent given to the old one is not a consent to the new.
/// </summary>
public static class MarketingWordings
{
    public const string Current = "marketing-2026-10";
}

// ---------- Commands and the query ----------

public sealed record GetPreferencesQuery : IQuery<PreferencesResult>;

public sealed record ChooseCultureCommand(string Culture) : ICommand<PreferencesResult>;

public sealed record SetMarketingConsentCommand(bool Given) : ICommand<PreferencesResult>;

public enum PreferencesOutcome
{
    Done,
    /// <summary>Signed in, and the identity not yet linked: nobody to keep preferences for.</summary>
    NoCustomer,
}

public sealed record PreferencesResult(PreferencesOutcome Outcome, PreferencesView? View) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        PreferencesOutcome.Done => AuditOutcome.Allowed,
        PreferencesOutcome.NoCustomer => AuditOutcome.Denied,
    };

    string? IAuditedResult.AuditReason => Outcome == PreferencesOutcome.Done ? null : Outcome.ToString();
}

// ---------- Refusals ----------

public static class PreferencesRefusals
{
    public static readonly Refusal NoCustomer = new(
        "NoCustomer", 409, "Tu cuenta todavía no está lista. Vuelve a entrar.", "Your account is not ready yet. Sign in again.");
}

// ---------- Validation ----------

public sealed class ChooseCultureValidator : AbstractValidator<ChooseCultureCommand>
{
    public ChooseCultureValidator() =>
        RuleFor(command => command.Culture)
            .Must(culture => CultureNegotiation.Supported.Contains(culture?.Trim().ToLowerInvariant()))
            .WithMessage($"The shop speaks {string.Join(" and ", CultureNegotiation.Supported)}.")
            .OverridePropertyName("culture");
}

// ---------- Endpoints ----------

public sealed class ManagePreferencesEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var preferences = app.MapGroup("/api/accounts/me/preferences")
            .RequireAuthorization(TenderoPolicyNames.Shopper)
            .WithTags("Accounts");

        preferences.MapGet("",
            async Task<Results<Ok<PreferencesView>, ProblemHttpResult>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                Answer(await dispatcher.SendAsync(new GetPreferencesQuery(), ct)))
            .WithName("GetPreferences")
            .WithMetadata(new Refuses(PreferencesRefusals.NoCustomer));

        preferences.MapPut("/culture",
            async Task<Results<Ok<PreferencesView>, ProblemHttpResult>> (CulturePreferenceRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Answer(await dispatcher.SendAsync(new ChooseCultureCommand(request.Culture ?? ""), ct)))
            .WithName("ChooseCulture")
            .WithMetadata(new Refuses(PreferencesRefusals.NoCustomer));

        preferences.MapPut("/marketing",
            async Task<Results<Ok<PreferencesView>, ProblemHttpResult>> (MarketingPreferenceRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                Answer(await dispatcher.SendAsync(new SetMarketingConsentCommand(request.Given), ct)))
            .WithName("SetMarketingConsent")
            .WithMetadata(new Refuses(PreferencesRefusals.NoCustomer));
    }

    private static Results<Ok<PreferencesView>, ProblemHttpResult> Answer(PreferencesResult result) => result.Outcome switch
    {
        PreferencesOutcome.Done => TypedResults.Ok(result.View!),
        PreferencesOutcome.NoCustomer => TypedResults.Problem(
            statusCode: PreferencesRefusals.NoCustomer.Status, extensions: PreferencesRefusals.NoCustomer.Said()),
    };
}

// ---------- Handlers ----------

public sealed class ManagePreferencesHandlers(
    ICustomerRepository customers,
    IPrincipalAccessor principals,
    IUnitOfWork unitOfWork,
    TimeProvider clock) :
    IQueryHandler<GetPreferencesQuery, PreferencesResult>,
    ICommandHandler<ChooseCultureCommand, PreferencesResult>,
    ICommandHandler<SetMarketingConsentCommand, PreferencesResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    public async Task<PreferencesResult> HandleAsync(GetPreferencesQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("accounts.preferences.read");
        return await FindAsync(cancellationToken) is { } customer
            ? new PreferencesResult(PreferencesOutcome.Done, PreferencesView.Of(customer))
            : new PreferencesResult(PreferencesOutcome.NoCustomer, null);
    }

    public Task<PreferencesResult> HandleAsync(ChooseCultureCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.preferences.culture", customer => customer.ChooseCulture(clock, command.Culture), cancellationToken);

    public Task<PreferencesResult> HandleAsync(SetMarketingConsentCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.preferences.marketing", customer =>
        {
            if (command.Given)
                customer.GiveMarketingConsent(clock, MarketingWordings.Current);
            else
                customer.WithdrawMarketingConsent(clock, MarketingWordings.Current);

            Activity.Current?.SetTag("accounts.marketing.given", command.Given);
        }, cancellationToken);

    private async Task<PreferencesResult> ChangeAsync(string span, Action<Customer> change, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity(span);

        if (await FindAsync(cancellationToken) is not { } customer)
        {
            activity?.SetTag("accounts.unlinked", true);
            return new PreferencesResult(PreferencesOutcome.NoCustomer, null);
        }

        change(customer);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new PreferencesResult(PreferencesOutcome.Done, PreferencesView.Of(customer));
    }

    /// <summary>The caller's own customer, and only one with an identity: a guest keeps no preferences.</summary>
    private async Task<Customer?> FindAsync(CancellationToken cancellationToken) =>
        principals.Current.Customer is { } id && await customers.FindByIdAsync(id, cancellationToken) is { IsGuest: false } customer
            ? customer
            : null;
}
