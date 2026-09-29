using System.Diagnostics;
using System.Text.RegularExpressions;
using Carter;
using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Accounts.Features.ManageTrail;

// A signed-in customer's trail (R-8, *Sigue donde lo dejaste* and *Tu historial*):
// recorded as they browse, merged once from the browser at sign-in, read back,
// removed one by one or all at once, and switched off. The rules are the
// aggregate's (BrowsingHistory); this slice fetches, applies and answers.
//
// **The customer is the caller, always.** No route or body names a customer: an
// endpoint that took one would read anybody's trail to whoever guessed a GUID,
// which is the reasoning `MyOrders` already wrote down.
//
// **Audited as every command is, with the contents redacted.** The pipeline
// writes a row per command (AuditStep) and a trail is personal data: the row
// says a customer recorded, merged or cleared their trail and when, and never
// which product or which words — an audit log that kept what the customer
// deleted would make "delete" a lie.

// ---------- The wire ----------

/// <summary>A product the customer viewed, by its public code.</summary>
public sealed record ViewedProduct(string Code, DateTimeOffset At);

/// <summary>Something the customer searched for, in their words.</summary>
public sealed record PastSearch(string Text, DateTimeOffset At);

/// <param name="Enabled">False when the customer switched it off: nothing is kept or recorded.</param>
/// <param name="Views">Most recent first, at most <see cref="BrowsingHistory.MaxViews"/>.</param>
/// <param name="Searches">Most recent first, at most <see cref="BrowsingHistory.MaxSearches"/>.</param>
public sealed record BrowsingHistoryView(bool Enabled, IReadOnlyList<ViewedProduct> Views, IReadOnlyList<PastSearch> Searches)
{
    public static BrowsingHistoryView Of(BrowsingTrail trail) => new(
        trail.IsEnabled,
        [.. trail.Views.Select(entry => new ViewedProduct(entry.Key, entry.At))],
        [.. trail.Searches.Select(entry => new PastSearch(entry.Key, entry.At))]);
}

public sealed record RecordViewRequest(string? Code);

public sealed record RecordSearchRequest(string? Text);

/// <summary>The browser's guest trail, sent once when its owner signs in, with the times it kept.</summary>
public sealed record MergeHistoryRequest(IReadOnlyList<ViewedProduct>? Views, IReadOnlyList<PastSearch>? Searches);

public sealed record HistorySettingsRequest(bool Enabled);

// ---------- Commands and the query ----------

public sealed record GetBrowsingHistoryQuery : IQuery<BrowsingHistoryView>;

/// <summary>A product viewed or a search made: the trail's passage, not a change to the shop, so not audited (<see cref="IUnauditedCommand"/>).</summary>
public sealed record RecordBrowsingCommand(BrowsingKind Kind, string Key) : ICommand<BrowsingHistoryView>, IUnauditedCommand;

public sealed record MergeBrowsingCommand(
    [property: AuditRedacted] IReadOnlyList<ViewedProduct> Views,
    [property: AuditRedacted] IReadOnlyList<PastSearch> Searches) : ICommand<BrowsingHistoryView>;

public sealed record ForgetBrowsingCommand(BrowsingKind Kind, [property: AuditRedacted] string Key) : ICommand<BrowsingHistoryView>;

public sealed record ClearBrowsingCommand : ICommand<BrowsingHistoryView>;

public sealed record SetBrowsingCommand(bool Enabled) : ICommand<BrowsingHistoryView>;

// ---------- Validation ----------

/// <summary>What a key may be, per kind — one place for the three commands that carry one.</summary>
public static partial class TrailKeyRules
{
    /// <summary>A search as short as the search box accepts one.</summary>
    public const int MinSearchLength = 2;

    public static bool IsCode(string? code) => code is not null && Code().IsMatch(code.Trim());

    public static bool IsSearch(string? text) =>
        text is not null && text.Trim().Length is >= MinSearchLength and <= BrowsingHistory.MaxSearchLength;

    public static bool IsKey(BrowsingKind kind, string? key) => kind == BrowsingKind.View ? IsCode(key) : IsSearch(key);

    public const string CodeMessage = "A product is its public code: letters and digits, up to 20.";

    public const string SearchMessage = "A search is 2 to 200 characters.";

    // Accounts references the SharedKernel only, so it does not borrow the
    // catalogue's exact alphabet: a code is an opaque value here, bounded so a
    // trail cannot be used to store anything else.
    [GeneratedRegex("^[A-Za-z0-9]{1,20}$")]
    private static partial Regex Code();
}

public sealed class RecordBrowsingValidator : AbstractValidator<RecordBrowsingCommand>
{
    public RecordBrowsingValidator()
    {
        RuleFor(command => command.Kind).IsInEnum();
        RuleFor(command => command.Key).Must(TrailKeyRules.IsCode).When(command => command.Kind == BrowsingKind.View)
            .WithMessage(TrailKeyRules.CodeMessage).OverridePropertyName("code");
        RuleFor(command => command.Key).Must(TrailKeyRules.IsSearch).When(command => command.Kind == BrowsingKind.Search)
            .WithMessage(TrailKeyRules.SearchMessage).OverridePropertyName("text");
    }
}

public sealed class ForgetBrowsingValidator : AbstractValidator<ForgetBrowsingCommand>
{
    public ForgetBrowsingValidator()
    {
        RuleFor(command => command.Kind).IsInEnum();
        RuleFor(command => command.Key).Must((command, key) => TrailKeyRules.IsKey(command.Kind, key))
            .WithMessage(command => command.Kind == BrowsingKind.View ? TrailKeyRules.CodeMessage : TrailKeyRules.SearchMessage)
            .OverridePropertyName("key");
    }
}

public sealed class MergeBrowsingValidator : AbstractValidator<MergeBrowsingCommand>
{
    public MergeBrowsingValidator()
    {
        // No more than the trail keeps: a merge that sent more would be dropped
        // by the cap anyway, and refusing it keeps a body from being a payload.
        RuleFor(command => command.Views.Count).LessThanOrEqualTo(BrowsingHistory.MaxViews).OverridePropertyName("views");
        RuleForEach(command => command.Views).ChildRules(view =>
        {
            view.RuleFor(entry => entry.Code).Must(TrailKeyRules.IsCode).WithMessage(TrailKeyRules.CodeMessage);
            view.RuleFor(entry => entry.At).NotEqual(default(DateTimeOffset));
        }).OverridePropertyName("views");

        RuleFor(command => command.Searches.Count).LessThanOrEqualTo(BrowsingHistory.MaxSearches).OverridePropertyName("searches");
        RuleForEach(command => command.Searches).ChildRules(search =>
        {
            search.RuleFor(entry => entry.Text).Must(TrailKeyRules.IsSearch).WithMessage(TrailKeyRules.SearchMessage);
            search.RuleFor(entry => entry.At).NotEqual(default(DateTimeOffset));
        }).OverridePropertyName("searches");
    }
}

// ---------- Endpoints ----------

public sealed class ManageTrailEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var history = app.MapGroup("/api/accounts/me/history")
            // A person's own trail, read only by their own session: a shopper, and
            // never an agent — not even one acting for them.
            .RequireAuthorization(TenderoPolicyNames.Shopper)
            .WithTags("Accounts");

        history.MapGet("",
            async Task<Ok<BrowsingHistoryView>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new GetBrowsingHistoryQuery(), ct)))
            .WithName("GetBrowsingHistory");

        history.MapPost("/views",
            async Task<Ok<BrowsingHistoryView>> (RecordViewRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new RecordBrowsingCommand(BrowsingKind.View, request.Code ?? ""), ct)))
            .WithName("RecordProductView");

        history.MapPost("/searches",
            async Task<Ok<BrowsingHistoryView>> (RecordSearchRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new RecordBrowsingCommand(BrowsingKind.Search, request.Text ?? ""), ct)))
            .WithName("RecordSearch");

        history.MapPost("/merge",
            async Task<Ok<BrowsingHistoryView>> (MergeHistoryRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new MergeBrowsingCommand(request.Views ?? [], request.Searches ?? []), ct)))
            .WithName("MergeBrowsingHistory");

        // DELETE /api/accounts/me/history/Search/zapatillas%20running
        // The key is the path's last segment, encoded by the page. Routing
        // decodes every escape but %2F, so a search holding a slash is given
        // back its slash here and nowhere else.
        history.MapDelete("/{kind}/{key}",
            async Task<Ok<BrowsingHistoryView>> (WireEnum<BrowsingKind> kind, string key, ICommandDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(
                    new ForgetBrowsingCommand(kind.Value, key.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)), ct)))
            .WithName("ForgetBrowsingEntry");

        history.MapDelete("",
            async Task<Ok<BrowsingHistoryView>> (ICommandDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ClearBrowsingCommand(), ct)))
            .WithName("ClearBrowsingHistory");

        history.MapPut("/settings",
            async Task<Ok<BrowsingHistoryView>> (HistorySettingsRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new SetBrowsingCommand(request.Enabled), ct)))
            .WithName("SetBrowsingHistory");
    }
}

// ---------- Handlers ----------

public sealed class GetBrowsingHistoryHandler(IBrowsingHistoryReader trails, IPrincipalAccessor principals, TimeProvider clock)
    : IQueryHandler<GetBrowsingHistoryQuery, BrowsingHistoryView>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    public async Task<BrowsingHistoryView> HandleAsync(GetBrowsingHistoryQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("accounts.browsing.read");

        // Signed in and not yet linked: nobody to have a trail. Empty rather than
        // refused, as `MyOrders` answers — the truthful answer, and no wiring told.
        if (principals.Current.Customer is not { } customer)
        {
            activity?.SetTag("accounts.unlinked", true);
            return BrowsingHistoryView.Of(BrowsingTrail.Empty);
        }

        var trail = await trails.ForCustomerAsync(customer, clock.GetUtcNow(), cancellationToken) ?? BrowsingTrail.Empty;

        activity?.SetTag("accounts.browsing.views", trail.Views.Count);
        activity?.SetTag("accounts.browsing.searches", trail.Searches.Count);

        return BrowsingHistoryView.Of(trail);
    }
}

public sealed class ManageTrailHandlers(
    IBrowsingHistoryRepository histories,
    IPrincipalAccessor principals,
    IUnitOfWork unitOfWork,
    TimeProvider clock) :
    ICommandHandler<RecordBrowsingCommand, BrowsingHistoryView>,
    ICommandHandler<MergeBrowsingCommand, BrowsingHistoryView>,
    ICommandHandler<ForgetBrowsingCommand, BrowsingHistoryView>,
    ICommandHandler<ClearBrowsingCommand, BrowsingHistoryView>,
    ICommandHandler<SetBrowsingCommand, BrowsingHistoryView>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Accounts);

    public Task<BrowsingHistoryView> HandleAsync(RecordBrowsingCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.browsing.record", command.Kind, create: true, (history, activity) =>
        {
            // Applied first and tagged after: `activity?.SetTag(…, Record(…))` would
            // skip the recording itself whenever nobody listens to the span.
            var recorded = history.Record(clock, command.Kind, command.Key);
            activity?.SetTag("accounts.browsing.recorded", recorded);
        }, cancellationToken);

    public Task<BrowsingHistoryView> HandleAsync(MergeBrowsingCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.browsing.merge", kind: null, create: true, (history, activity) =>
        {
            var views = history.Merge(clock, BrowsingKind.View, command.Views.Select(view => new TrailEntry(view.Code, view.At)));
            var searches = history.Merge(clock, BrowsingKind.Search, command.Searches.Select(search => new TrailEntry(search.Text, search.At)));

            activity?.SetTag("accounts.browsing.merged_views", views);
            activity?.SetTag("accounts.browsing.merged_searches", searches);
        }, cancellationToken);

    public Task<BrowsingHistoryView> HandleAsync(ForgetBrowsingCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.browsing.forget", command.Kind, create: false, (history, activity) =>
        {
            var forgotten = history.Forget(clock, command.Kind, command.Key);
            activity?.SetTag("accounts.browsing.forgotten", forgotten);
        }, cancellationToken);

    public Task<BrowsingHistoryView> HandleAsync(ClearBrowsingCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.browsing.clear", kind: null, create: false, (history, _) => history.Clear(clock), cancellationToken);

    /// <summary>A switch turned off is kept even for a customer with no trail yet, so nothing is recorded afterwards.</summary>
    public Task<BrowsingHistoryView> HandleAsync(SetBrowsingCommand command, CancellationToken cancellationToken) =>
        ChangeAsync("accounts.browsing.settings", kind: null, create: !command.Enabled, (history, activity) =>
        {
            if (command.Enabled)
                history.SwitchOn(clock);
            else
                history.SwitchOff(clock);

            activity?.SetTag("accounts.browsing.enabled", command.Enabled);
        }, cancellationToken);

    /// <summary>
    /// Load the caller's trail — starting one when <paramref name="create"/> says
    /// the change needs somewhere to go — apply the change, save, and answer what
    /// is kept now. Never the words or the codes on the span: the trail is
    /// personal data, and a trace is exported further than a table.
    /// </summary>
    private async Task<BrowsingHistoryView> ChangeAsync(
        string span, BrowsingKind? kind, bool create, Action<BrowsingHistory, Activity?> change, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity(span);
        if (kind is { } set)
            activity?.SetTag("accounts.browsing.kind", set.ToString());

        if (principals.Current.Customer is not { } customer)
        {
            activity?.SetTag("accounts.unlinked", true);
            return BrowsingHistoryView.Of(BrowsingTrail.Empty);
        }

        var history = await histories.FindAsync(customer, cancellationToken);

        if (history is null)
        {
            if (!create)
                return BrowsingHistoryView.Of(BrowsingTrail.Empty);

            history = BrowsingHistory.StartFor(customer, clock);
            histories.Add(history);
        }

        change(history, activity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BrowsingHistoryView.Of(BrowsingTrail.Of(history, clock.GetUtcNow()));
    }
}
