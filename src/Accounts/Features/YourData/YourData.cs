using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Carter;
using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Accounts.Features.YourData;

// The shopper's own data (P15-9, docs/specs/shopper-account.md, decision 9): see
// what the shop holds, in one download, and remove the account.
//
// **Every context says what it holds for itself** (IPersonalDataHolder): the
// address book and the trail in Accounts, the wallet and the orders in Ordering.
// This slice gathers them and owns only the customer. Removing the account
// forgets what may be forgotten, keeps what the law makes the shop keep — the
// orders, blocked (LOPDGDD art. 32) — and anonymises the customer, all in one
// save. The identity at the sign-in provider is not the shop's, and the page says
// so.

// ---------- Options ----------

public sealed class PrivacyOptions
{
    public const string SectionName = "Privacy";

    /// <summary>
    /// How long an order is kept after the account is removed: six years, Spain's
    /// Commercial Code (art. 30), confirmed by the author on 2026-10-20. The shop's
    /// to configure; docs/privacy.md states it.
    /// </summary>
    public int OrderRetentionYears { get; set; } = 6;
}

// ---------- The wire ----------

/// <param name="DisplayName">The name the sign-in provider last sent: a cache, not the shop's own.</param>
public sealed record AccountData(
    string? DisplayName, string Culture, bool CultureChosen, bool? MarketingGiven, DateTimeOffset? MarketingAt,
    string? MarketingWording, DateTimeOffset CreatedAt);

/// <param name="Sections">Each holder's share by its name — <c>addresses</c>, <c>browsingHistory</c>, <c>paymentMethods</c>, <c>orders</c> — only those that hold something.</param>
public sealed record PersonalDataExport(DateTimeOffset GeneratedAt, AccountData Account, IReadOnlyDictionary<string, JsonElement> Sections);

/// <param name="Forgotten">The sections forgotten at once.</param>
/// <param name="Kept">The sections kept because the law makes the shop keep them, and used for nothing else.</param>
/// <param name="KeptForYears">How long what is kept is kept.</param>
public sealed record AccountRemovalView(IReadOnlyList<string> Forgotten, IReadOnlyList<string> Kept, int KeptForYears);

// ---------- Query and command ----------

public sealed record ExportPersonalDataQuery : IQuery<PersonalDataExport?>;

public sealed record RemoveAccountCommand : ICommand<AccountRemovalResult>;

public enum AccountRemovalOutcome
{
    Removed,
    /// <summary>Signed in, and no customer of their own: nothing to remove.</summary>
    NoCustomer,
}

public sealed record AccountRemovalResult(AccountRemovalOutcome Outcome, AccountRemovalView? View) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        AccountRemovalOutcome.Removed => AuditOutcome.Allowed,
        AccountRemovalOutcome.NoCustomer => AuditOutcome.Denied,
    };

    string? IAuditedResult.AuditReason => Outcome == AccountRemovalOutcome.Removed ? null : Outcome.ToString();
}

// ---------- Refusals ----------

public static class YourDataRefusals
{
    public static readonly Refusal NoCustomer = new(
        "NoCustomer", 409, "Tu cuenta todavía no está lista. Vuelve a entrar.", "Your account is not ready yet. Sign in again.");
}

// ---------- Endpoints ----------

public sealed class YourDataEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/accounts/me")
            .RequireAuthorization(TenderoPolicyNames.Shopper)
            .WithTags("Accounts");

        me.MapGet("/data",
            async Task<Results<Ok<PersonalDataExport>, ProblemHttpResult>> (HttpContext http, IQueryDispatcher dispatcher, CancellationToken ct) =>
            {
                if (await dispatcher.SendAsync(new ExportPersonalDataQuery(), ct) is not { } export)
                    return TypedResults.Problem(statusCode: YourDataRefusals.NoCustomer.Status, extensions: YourDataRefusals.NoCustomer.Said());

                // A download, named for what it is; the shop keeps no copy of it.
                http.Response.Headers.ContentDisposition = $"attachment; filename=\"tendero-your-data-{export.GeneratedAt:yyyy-MM-dd}.json\"";
                return TypedResults.Ok(export);
            })
            .WithName("ExportPersonalData")
            .WithMetadata(new Refuses(YourDataRefusals.NoCustomer));

        me.MapDelete("",
            async Task<Results<Ok<AccountRemovalView>, ProblemHttpResult>> (ICommandDispatcher dispatcher, CancellationToken ct) =>
                await dispatcher.SendAsync(new RemoveAccountCommand(), ct) switch
                {
                    { Outcome: AccountRemovalOutcome.Removed, View: { } view } => TypedResults.Ok(view),
                    _ => TypedResults.Problem(statusCode: YourDataRefusals.NoCustomer.Status, extensions: YourDataRefusals.NoCustomer.Said()),
                })
            .WithName("RemoveAccount")
            .WithMetadata(new Refuses(YourDataRefusals.NoCustomer));
    }
}

// ---------- Handlers ----------

public sealed class YourDataHandlers(
    ICustomerRepository customers,
    IEnumerable<IPersonalDataHolder> holders,
    IPrincipalAccessor principals,
    IUnitOfWork unitOfWork,
    IOptions<PrivacyOptions> options,
    TimeProvider clock) :
    IQueryHandler<ExportPersonalDataQuery, PersonalDataExport?>,
    ICommandHandler<RemoveAccountCommand, AccountRemovalResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    private static readonly Meter Meter = new(TelemetrySources.Accounts);

    /// <summary>The tracking plan's evidence that erasure is used (P15-10): a count, with no identity.</summary>
    private static readonly Counter<long> Erased = Meter.CreateCounter<long>(
        "tendero.accounts.erased", unit: "{account}", description: "Accounts removed by their owner.");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PersonalDataExport?> HandleAsync(ExportPersonalDataQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("accounts.personal_data.export");

        if (await FindAsync(cancellationToken) is not { } customer)
            return null;

        var sections = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var holder in holders)
        {
            if (await holder.ExportAsync(customer.Id, cancellationToken) is { } data)
                sections[holder.Section] = JsonSerializer.SerializeToElement(data, Json);
        }

        activity?.SetTag("accounts.personal_data.sections", sections.Count);

        return new PersonalDataExport(
            clock.GetUtcNow(),
            new AccountData(
                customer.DisplayName, customer.Culture, customer.CultureChosen,
                customer.Marketing?.Given, customer.Marketing?.At, customer.Marketing?.Wording, customer.CreatedAt),
            sections);
    }

    /// <summary>
    /// Every holder forgets or keeps its share, the customer is anonymised, and it is
    /// all saved once: the account is removed whole or not at all. The audit row says
    /// an account was removed and when — its own command is the record.
    /// </summary>
    public async Task<AccountRemovalResult> HandleAsync(RemoveAccountCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("accounts.personal_data.remove");

        if (await FindAsync(cancellationToken) is not { } customer)
            return new AccountRemovalResult(AccountRemovalOutcome.NoCustomer, null);

        var erasures = new List<Erasure>();
        foreach (var holder in holders)
            erasures.Add(await holder.EraseAsync(customer.Id, cancellationToken));

        customer.Erase(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        Erased.Add(1);

        var forgotten = erasures.Where(erasure => erasure.Forgotten).Select(erasure => erasure.Section).Prepend("account").ToList();
        var kept = erasures.Where(erasure => erasure.Kept).Select(erasure => erasure.Section).ToList();
        activity?.SetTag("accounts.personal_data.forgotten", forgotten.Count);
        activity?.SetTag("accounts.personal_data.kept", kept.Count);

        return new AccountRemovalResult(
            AccountRemovalOutcome.Removed, new AccountRemovalView(forgotten, kept, options.Value.OrderRetentionYears));
    }

    /// <summary>The caller's own customer, signed in and not already erased.</summary>
    private async Task<Customer?> FindAsync(CancellationToken cancellationToken) =>
        principals.Current.Customer is { } id && await customers.FindByIdAsync(id, cancellationToken) is { IsGuest: false, IsErased: false } customer
            ? customer
            : null;
}
