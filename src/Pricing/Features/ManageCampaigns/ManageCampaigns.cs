using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Pricing.Domain;
using ElGuerre.Tendero.Pricing.Ports;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Pricing.Features.ManageCampaigns;

// The shopkeeper's campaigns (R-8, H-1; ADR 0044): created as drafts, scheduled
// for their window, unscheduled to be edited, ended early. Every change is a
// command, so every change is an audit row with the shopkeeper's name on it.

// ---------- The views ----------

/// <summary>Both languages at once, as the shopkeeper writes them (invariant 6: es AND en).</summary>
public sealed record BilingualText(string Es, string En)
{
    public static BilingualText From(LocalizedText text) => new(text.In("es"), text.In("en"));

    public LocalizedText ToLocalizedText() =>
        new(new Dictionary<string, string> { ["es"] = Es.Trim(), ["en"] = En.Trim() });
}

public sealed record CampaignImageView(string ImageId, BilingualText Alt);

/// <summary>
/// A campaign as the backoffice edits it: both languages, the stored status and
/// the window's phase side by side. Status is what the shopkeeper decided
/// (Draft, Scheduled, Ended); phase is where the clock stands in the window
/// (Upcoming, Running, Over). The screen reads both — a scheduled campaign whose
/// phase is Running is the one on the shop now.
/// </summary>
public sealed record ManagedCampaignView(
    string Code,
    BilingualText Name,
    BilingualText Line,
    CampaignKind Kind,
    CampaignScope Scope,
    string? CategoryCode,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int Priority,
    IReadOnlyList<string> PromotionCodes,
    CampaignImageView? Image,
    CampaignStatus Status,
    CampaignPhase Phase,
    DateTimeOffset? EndedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ManagedCampaignView From(Campaign campaign, DateTimeOffset now) => new(
        campaign.Code.Value,
        BilingualText.From(campaign.Name),
        BilingualText.From(campaign.Line),
        campaign.Kind,
        campaign.Scope,
        campaign.CategoryCode,
        campaign.StartsAt,
        campaign.EndsAt,
        campaign.Priority,
        campaign.PromotionCodes,
        campaign.Image is { } image ? new CampaignImageView(image.Id.Value, BilingualText.From(image.Alt)) : null,
        campaign.Status,
        campaign.PhaseAt(now),
        campaign.EndedAt,
        campaign.CreatedAt,
        campaign.UpdatedAt);
}

// ---------- The reads ----------

public sealed record ListManagedCampaignsQuery : IQuery<IReadOnlyList<ManagedCampaignView>>;

public sealed record GetManagedCampaignQuery(CampaignCode Code) : IQuery<ManagedCampaignView?>;

public sealed class ManagedCampaignsHandler(ICampaignRepository campaigns, TimeProvider clock) :
    IQueryHandler<ListManagedCampaignsQuery, IReadOnlyList<ManagedCampaignView>>,
    IQueryHandler<GetManagedCampaignQuery, ManagedCampaignView?>
{
    public async Task<IReadOnlyList<ManagedCampaignView>> HandleAsync(ListManagedCampaignsQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return [.. (await campaigns.AllAsync(cancellationToken)).Select(campaign => ManagedCampaignView.From(campaign, now))];
    }

    public async Task<ManagedCampaignView?> HandleAsync(GetManagedCampaignQuery query, CancellationToken cancellationToken) =>
        await campaigns.FindAsync(query.Code, cancellationToken) is { } campaign
            ? ManagedCampaignView.From(campaign, clock.GetUtcNow())
            : null;
}

// ---------- The changes ----------

public enum CampaignChange
{
    Created,
    Updated,
    Scheduled,
    Unscheduled,
    Ended,

    /// <summary>Nothing to do: already so. Not a refusal.</summary>
    Unchanged,

    NotFound,

    /// <summary>Creating a code that is already taken.</summary>
    AlreadyExists,

    /// <summary>A promotion code Pricing does not know.</summary>
    UnknownPromotion,

    /// <summary>An image id the store does not hold.</summary>
    UnknownImage,

    /// <summary>A window that has already closed, set or scheduled.</summary>
    WindowClosed,

    /// <summary>Editing a campaign that is not a draft: unschedule it first.</summary>
    NotEditable,

    /// <summary>A move the transition table forbids — anything out of Ended.</summary>
    NotAllowed,
}

/// <param name="Detail">The server's words for a refusal, when the change alone does not say enough (which codes were unknown).</param>
public sealed record CampaignResult(CampaignChange Change, ManagedCampaignView? Campaign, string? Detail = null) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome => IsRefusal(Change) ? AuditOutcome.Denied : AuditOutcome.Allowed;

    string? IAuditedResult.AuditReason => IsRefusal(Change) ? Detail ?? Change.ToString() : null;

    private static bool IsRefusal(CampaignChange change) => change switch
    {
        CampaignChange.Created or CampaignChange.Updated or CampaignChange.Scheduled
            or CampaignChange.Unscheduled or CampaignChange.Ended or CampaignChange.Unchanged => false,
        CampaignChange.NotFound or CampaignChange.AlreadyExists or CampaignChange.UnknownPromotion
            or CampaignChange.UnknownImage or CampaignChange.WindowClosed or CampaignChange.NotEditable
            or CampaignChange.NotAllowed => true,
    };
}

public sealed record CreateCampaignCommand(CampaignCode Code, CampaignDetails Details) : ICommand<CampaignResult>;

public sealed record UpdateCampaignCommand(CampaignCode Code, CampaignDetails Details) : ICommand<CampaignResult>;

public sealed record ScheduleCampaignCommand(CampaignCode Code) : ICommand<CampaignResult>;

public sealed record UnscheduleCampaignCommand(CampaignCode Code) : ICommand<CampaignResult>;

public sealed record EndCampaignCommand(CampaignCode Code) : ICommand<CampaignResult>;

/// <summary>
/// What a campaign's details must be before any rule sees them — a form check.
/// The rules that need the clock or the promotions (a window already closed, a
/// code nobody knows) are the handler's, and are audited as refusals.
/// </summary>
public sealed class CampaignDetailsValidator : AbstractValidator<CampaignDetails>
{
    public const int MaxName = 80;
    public const int MaxLine = 200;
    public const int MaxPromotions = 20;
    public const int MaxPriority = 1000;

    public CampaignDetailsValidator()
    {
        RuleFor(details => details.Name).Must(text => InBoth(text, MaxName))
            .WithMessage($"A campaign's name is written in es and en, at most {MaxName} characters each.");
        RuleFor(details => details.Line).Must(text => InBoth(text, MaxLine))
            .WithMessage($"A campaign's line is written in es and en, at most {MaxLine} characters each.");
        RuleFor(details => details.Kind).IsInEnum();
        RuleFor(details => details.CategoryCode).MaximumLength(60);
        RuleFor(details => details.EndsAt).GreaterThan(details => details.StartsAt)
            .WithMessage("A campaign's window ends after it starts.");
        RuleFor(details => details.Priority).InclusiveBetween(0, MaxPriority);
        RuleFor(details => details.PromotionCodes).NotEmpty()
            .WithMessage("A campaign groups at least one promotion.");
        RuleFor(details => details.PromotionCodes.Count).LessThanOrEqualTo(MaxPromotions)
            .OverridePropertyName(nameof(CampaignDetails.PromotionCodes));
        RuleForEach(details => details.PromotionCodes).NotEmpty().MaximumLength(40);
        RuleFor(details => details.Image!.Id.Value).NotEmpty().MaximumLength(128)
            .OverridePropertyName("Image.ImageId")
            .When(details => details.Image is not null);
        RuleFor(details => details.Image!.Alt).Must(text => InBoth(text, MaxLine))
            .WithMessage($"An image's alt text is written in es and en, at most {MaxLine} characters each.")
            .OverridePropertyName("Image.Alt")
            .When(details => details.Image is not null);
    }

    private static bool InBoth(LocalizedText? text, int maxLength) =>
        text is not null
        && CultureNegotiation.Supported.All(culture =>
            text.Values.TryGetValue(culture, out var value)
            && !string.IsNullOrWhiteSpace(value)
            && value.Length <= maxLength);
}

public sealed class CreateCampaignValidator : AbstractValidator<CreateCampaignCommand>
{
    public CreateCampaignValidator() => RuleFor(command => command.Details).SetValidator(new CampaignDetailsValidator());
}

public sealed class UpdateCampaignValidator : AbstractValidator<UpdateCampaignCommand>
{
    public UpdateCampaignValidator() => RuleFor(command => command.Details).SetValidator(new CampaignDetailsValidator());
}

/// <summary>
/// The five changes a shopkeeper makes to a campaign. One handler because they
/// share the load, the not-found, the save and the answer, as the agents'
/// registry does.
/// </summary>
public sealed class ManageCampaignsHandler(
    ICampaignRepository campaigns,
    IPromotionReader promotions,
    IImageStore images,
    IUnitOfWork unitOfWork,
    TimeProvider clock) :
    ICommandHandler<CreateCampaignCommand, CampaignResult>,
    ICommandHandler<UpdateCampaignCommand, CampaignResult>,
    ICommandHandler<ScheduleCampaignCommand, CampaignResult>,
    ICommandHandler<UnscheduleCampaignCommand, CampaignResult>,
    ICommandHandler<EndCampaignCommand, CampaignResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Pricing);

    public async Task<CampaignResult> HandleAsync(CreateCampaignCommand command, CancellationToken cancellationToken)
    {
        using var activity = Start("pricing.create_campaign", command.Code);

        if (await campaigns.FindAsync(command.Code, cancellationToken) is not null)
            return new CampaignResult(CampaignChange.AlreadyExists, null, $"Campaign '{command.Code}' already exists.");

        if (await RefusalOfAsync(command.Details, cancellationToken) is { } refusal)
            return refusal;

        var campaign = Campaign.Create(clock, command.Code, command.Details);
        campaigns.Add(campaign);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Answer(CampaignChange.Created, campaign);
    }

    public async Task<CampaignResult> HandleAsync(UpdateCampaignCommand command, CancellationToken cancellationToken)
    {
        using var activity = Start("pricing.update_campaign", command.Code);

        var campaign = await campaigns.FindAsync(command.Code, cancellationToken);
        if (campaign is null)
            return new CampaignResult(CampaignChange.NotFound, null);

        if (!campaign.IsEditable)
            return new CampaignResult(CampaignChange.NotEditable, ManagedCampaignView.From(campaign, clock.GetUtcNow()),
                $"Campaign '{command.Code}' is {campaign.Status}; only a draft is edited — unschedule it first.");

        if (await RefusalOfAsync(command.Details, cancellationToken) is { } refusal)
            return refusal;

        campaign.Update(clock, command.Details);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Answer(CampaignChange.Updated, campaign);
    }

    public Task<CampaignResult> HandleAsync(ScheduleCampaignCommand command, CancellationToken cancellationToken) =>
        MoveAsync(command.Code, "pricing.schedule_campaign", CampaignStatus.Scheduled, CampaignChange.Scheduled,
            campaign =>
                Campaign.IsOpenWindow(campaign.StartsAt, campaign.EndsAt, clock.GetUtcNow())
                    ? null
                    : new CampaignResult(CampaignChange.WindowClosed, ManagedCampaignView.From(campaign, clock.GetUtcNow()),
                        $"Campaign '{campaign.Code}' closed at {campaign.EndsAt:O}; a window that is over cannot be scheduled."),
            campaign => campaign.Schedule(clock),
            cancellationToken);

    public Task<CampaignResult> HandleAsync(UnscheduleCampaignCommand command, CancellationToken cancellationToken) =>
        MoveAsync(command.Code, "pricing.unschedule_campaign", CampaignStatus.Draft, CampaignChange.Unscheduled,
            _ => null, campaign => campaign.Unschedule(clock), cancellationToken);

    public Task<CampaignResult> HandleAsync(EndCampaignCommand command, CancellationToken cancellationToken) =>
        MoveAsync(command.Code, "pricing.end_campaign", CampaignStatus.Ended, CampaignChange.Ended,
            _ => null, campaign => campaign.End(clock), cancellationToken);

    /// <summary>
    /// The three moves between statuses. Asking for the status a campaign already
    /// has is not an error and not a change either — no event, no save, the answer
    /// a second click deserves. A move the table has no edge for is refused with
    /// the table's words rather than thrown.
    /// </summary>
    private async Task<CampaignResult> MoveAsync(
        CampaignCode code, string operation, CampaignStatus to, CampaignChange moved,
        Func<Campaign, CampaignResult?> refusal, Action<Campaign> move, CancellationToken cancellationToken)
    {
        using var activity = Start(operation, code);

        var campaign = await campaigns.FindAsync(code, cancellationToken);
        if (campaign is null)
            return new CampaignResult(CampaignChange.NotFound, null);

        if (campaign.Status == to)
            return Answer(CampaignChange.Unchanged, campaign);

        if (!campaign.CanMoveTo(to))
            return new CampaignResult(CampaignChange.NotAllowed, ManagedCampaignView.From(campaign, clock.GetUtcNow()),
                $"Campaign '{code}' is {campaign.Status} and cannot become {to}.");

        if (refusal(campaign) is { } refused)
            return refused;

        move(campaign);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Answer(moved, campaign);
    }

    /// <summary>
    /// The rules a form cannot check: the window against the clock, the codes
    /// against the promotions Pricing knows (ADR 0044: checked when the campaign
    /// is saved), and the picture against the store.
    /// </summary>
    private async Task<CampaignResult?> RefusalOfAsync(CampaignDetails details, CancellationToken cancellationToken)
    {
        if (!Campaign.IsOpenWindow(details.StartsAt, details.EndsAt, clock.GetUtcNow()))
            return new CampaignResult(CampaignChange.WindowClosed, null,
                $"The window closes at {details.EndsAt:O}, which has already passed.");

        var known = (await promotions.AllAsync(cancellationToken))
            .Select(promotion => promotion.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknown = details.PromotionCodes.Where(code => !known.Contains(code.Trim())).ToArray();
        if (unknown.Length > 0)
            return new CampaignResult(CampaignChange.UnknownPromotion, null,
                $"Unknown promotion code(s): {string.Join(", ", unknown)}.");

        if (details.Image is { } image)
        {
            var stored = await images.OpenAsync(image.Id, cancellationToken);
            if (stored is null)
                return new CampaignResult(CampaignChange.UnknownImage, null, $"No image '{image.Id}' is stored.");

            await stored.Content.DisposeAsync();
        }

        return null;
    }

    private CampaignResult Answer(CampaignChange change, Campaign campaign) =>
        new(change, ManagedCampaignView.From(campaign, clock.GetUtcNow()));

    private static Activity? Start(string operation, CampaignCode code)
    {
        var activity = Telemetry.StartActivity(operation);
        activity?.SetTag("pricing.campaign", code.Value);
        return activity;
    }
}

// ---------- The wire ----------

public sealed record CampaignImageRequest(string ImageId, BilingualText Alt);

/// <param name="CategoryCode">Null or absent for the whole shop.</param>
/// <param name="StartsAt">Inclusive.</param>
/// <param name="EndsAt">Exclusive; after <paramref name="StartsAt"/>, and not already past.</param>
/// <param name="Priority">0 to 1000; higher leads when two run at once.</param>
/// <param name="PromotionCodes">Codes of promotions Pricing knows (<c>GET /api/pricing/promotions</c>).</param>
/// <param name="Image">A picture uploaded through <c>POST /api/pricing/campaign-images</c>, or none.</param>
public sealed record CampaignDetailsRequest(
    BilingualText Name,
    BilingualText Line,
    CampaignKind Kind,
    string? CategoryCode,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int Priority,
    IReadOnlyList<string> PromotionCodes,
    CampaignImageRequest? Image = null)
{
    public CampaignDetails ToDetails() => new(
        Name.ToLocalizedText(),
        Line.ToLocalizedText(),
        Kind,
        CategoryCode,
        StartsAt,
        EndsAt,
        Priority,
        PromotionCodes ?? [],
        Image is { } image ? new CampaignImage(new ImageId(image.ImageId.Trim()), image.Alt.ToLocalizedText()) : null);
}

/// <param name="Code">Upper snake case, as a promotion's: TENDERO_DAYS. It never changes once created.</param>
public sealed record CreateCampaignRequest(string Code, CampaignDetailsRequest Details);

public sealed record CampaignChangeResponse(CampaignChange Change, ManagedCampaignView Campaign);

/// <summary>
/// What a change to a campaign is refused for, by code. A 400 is one the caller
/// can fix in the request; a 409 is about the campaign's state, or its code
/// being taken.
/// </summary>
public static class CampaignRefusals
{
    public static readonly Refusal InvalidCode = new(
        "InvalidCampaignCode", 400,
        "Eso no es un código de campaña: mayúsculas con guiones bajos, una letra primero (TENDERO_DAYS).",
        "That is not a campaign code: upper snake case, a letter first (TENDERO_DAYS).");

    public static readonly Refusal UnknownPromotion = new(
        "UnknownPromotion", 400, "La campaña nombra una promoción que no existe.", "The campaign names a promotion that does not exist.");

    public static readonly Refusal UnknownImage = new(
        "UnknownImage", 400, "La campaña nombra una imagen que no está guardada.", "The campaign names a picture that is not stored.");

    public static readonly Refusal WindowClosed = new(
        "WindowClosed", 400, "La ventana de la campaña no es válida o ya ha pasado.", "The campaign's window is not valid, or has passed.");

    public static readonly Refusal AlreadyExists = new(
        "AlreadyExists", 409, "Ya hay una campaña con ese código.", "There is already a campaign with that code.");

    public static readonly Refusal NotEditable = new(
        "NotEditable", 409, "La campaña ya no se puede editar en el estado en que está.", "The campaign can no longer be edited in the state it is in.");

    public static readonly Refusal NotAllowed = new(
        "NotAllowed", 409, "La campaña no admite ese paso desde donde está.", "The campaign does not take that step from where it is.");

    /// <summary>Every refusal a change to a campaign has: each of the moves can answer each.</summary>
    public static readonly Refuses All = new(
        InvalidCode, UnknownPromotion, UnknownImage, WindowClosed, AlreadyExists, NotEditable, NotAllowed);

    public static Refusal Of(CampaignChange change) => change switch
    {
        CampaignChange.UnknownPromotion => UnknownPromotion,
        CampaignChange.UnknownImage => UnknownImage,
        CampaignChange.WindowClosed => WindowClosed,
        CampaignChange.AlreadyExists => AlreadyExists,
        CampaignChange.NotEditable => NotEditable,
        _ => NotAllowed,
    };
}

public sealed class ManageCampaignsEndpoints : ICarterModule
{
    /// <summary>
    /// The backoffice's root. Not <c>/api/pricing/campaigns</c>, which is the
    /// shop's public list: one path cannot answer two audiences, and a literal
    /// under it (<c>…/campaigns/all</c>) would shadow a campaign of that code.
    /// </summary>
    public const string Root = "/api/pricing/campaign-calendar";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var calendar = app.MapGroup(Root)
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Pricing");

        calendar.MapGet("/", async Task<Ok<IReadOnlyList<ManagedCampaignView>>> (IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ListManagedCampaignsQuery(), ct)))
            .WithName("ListManagedCampaigns");

        calendar.MapGet("/{code}", async Task<Results<Ok<ManagedCampaignView>, NotFound>> (
                string code, IQueryDispatcher dispatcher, CancellationToken ct) =>
                CampaignCode.TryParse(code, out var parsed)
                && await dispatcher.SendAsync(new GetManagedCampaignQuery(parsed), ct) is { } campaign
                    ? TypedResults.Ok(campaign)
                    : TypedResults.NotFound())
            .WithName("GetManagedCampaign");

        calendar.MapPost("/", async Task<Results<Created<CampaignChangeResponse>, NotFound, ProblemHttpResult>> (
                CreateCampaignRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
            {
                if (!CampaignCode.TryParse(request.Code, out var code))
                    return TypedResults.Problem(
                        statusCode: CampaignRefusals.InvalidCode.Status,
                        extensions: CampaignRefusals.InvalidCode.Said(
                            es: $"«{request.Code}» no lo es; como mucho {CampaignCode.MaxLength} caracteres.",
                            en: $"'{request.Code}' is not one; at most {CampaignCode.MaxLength} characters.",
                            path: "$.code"));

                // 201 and where the campaign is: the one move here that makes
                // something with an address of its own.
                var result = await dispatcher.SendAsync(new CreateCampaignCommand(code, request.Details.ToDetails()), ct);
                return Http(result) switch
                {
                    { Result: Ok<CampaignChangeResponse> made } => TypedResults.Created($"/api/pricing/campaign-calendar/{code}", made.Value),
                    { Result: ProblemHttpResult refused } => refused,
                    _ => TypedResults.NotFound(),
                };
            })
            .WithMetadata(CampaignRefusals.All)
            .WithName("CreateCampaign");

        calendar.MapPut("/{code}", async Task<Results<Ok<CampaignChangeResponse>, NotFound, ProblemHttpResult>> (
                string code, CampaignDetailsRequest request, ICommandDispatcher dispatcher, CancellationToken ct) =>
                CampaignCode.TryParse(code, out var parsed)
                    ? Http(await dispatcher.SendAsync(new UpdateCampaignCommand(parsed, request.ToDetails()), ct))
                    : TypedResults.NotFound())
            .WithMetadata(CampaignRefusals.All)
            .WithName("UpdateCampaign");

        calendar.MapPost("/{code}/schedule", (string code, ICommandDispatcher dispatcher, CancellationToken ct) =>
                MoveAsync(code, parsed => new ScheduleCampaignCommand(parsed), dispatcher, ct))
            .WithMetadata(CampaignRefusals.All)
            .WithName("ScheduleCampaign");

        calendar.MapPost("/{code}/unschedule", (string code, ICommandDispatcher dispatcher, CancellationToken ct) =>
                MoveAsync(code, parsed => new UnscheduleCampaignCommand(parsed), dispatcher, ct))
            .WithMetadata(CampaignRefusals.All)
            .WithName("UnscheduleCampaign");

        calendar.MapPost("/{code}/end", (string code, ICommandDispatcher dispatcher, CancellationToken ct) =>
                MoveAsync(code, parsed => new EndCampaignCommand(parsed), dispatcher, ct))
            .WithMetadata(CampaignRefusals.All)
            .WithName("EndCampaign");
    }

    private static async Task<Results<Ok<CampaignChangeResponse>, NotFound, ProblemHttpResult>> MoveAsync(
        string code, Func<CampaignCode, ICommand<CampaignResult>> command, ICommandDispatcher dispatcher, CancellationToken ct) =>
        CampaignCode.TryParse(code, out var parsed)
            ? Http(await dispatcher.SendAsync(command(parsed), ct))
            : TypedResults.NotFound();

    /// <summary>
    /// A refusal the caller can fix in the request is a 400; one about the
    /// campaign's state or its code being taken is a 409; not there is a 404.
    /// </summary>
    private static Results<Ok<CampaignChangeResponse>, NotFound, ProblemHttpResult> Http(CampaignResult result) =>
        result.Change switch
        {
            CampaignChange.NotFound => TypedResults.NotFound(),
            // The change is the code; what the campaign itself said is the reason.
            CampaignChange.UnknownPromotion or CampaignChange.UnknownImage or CampaignChange.WindowClosed
                or CampaignChange.AlreadyExists or CampaignChange.NotEditable or CampaignChange.NotAllowed =>
                TypedResults.Problem(
                    statusCode: CampaignRefusals.Of(result.Change).Status,
                    extensions: CampaignRefusals.Of(result.Change).Said(reason: result.Detail)),
            _ => TypedResults.Ok(new CampaignChangeResponse(result.Change, result.Campaign!)),
        };
}
