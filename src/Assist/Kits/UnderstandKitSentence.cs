using System.Diagnostics;
using System.Text.Json;
using ElGuerre.Tendero.AI;
using ElGuerre.Tendero.AI.Chat;
using ElGuerre.Tendero.Assist.Tino;
using ElGuerre.Tendero.Assist.Turns;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Assist.Kits;

/// <param name="Sentence">What the shopper wrote: "un kit de cocina para inducción por menos de 200 euros".</param>
/// <param name="Culture">The language it was written in, and the one the kits are read in.</param>
public sealed record UnderstandKitSentenceQuery(string Sentence, string Culture) : IQuery<KitSentenceUnderstood>
{
    /// <summary>A sentence, not a letter: what a box above a form takes.</summary>
    public const int MaxLength = 300;
}

/// <summary>
/// What a kit's form is filled with (phase 13c, K-6). Not a kit: the page
/// shows the form filled, and the solver builds from it as from any other.
/// </summary>
/// <param name="Template">One of the shop's kits by its code, or none.</param>
/// <param name="Budget">The budget the sentence says, or none: the shopper types it.</param>
/// <param name="Requirements">What the sentence asks the kit to work with, among what that kit offers.</param>
/// <param name="Understood">Whether a kit was named. When not, the page shows the form as it was: this is never a refusal.</param>
public sealed record KitSentenceUnderstood(string? Template, Money? Budget, IReadOnlyList<string> Requirements, bool Understood)
{
    /// <summary>Nothing understood. A property for the reason <c>KitSentenceReading.PriceCues</c> is one.</summary>
    public static KitSentenceUnderstood Not => new(null, null, [], false);
}

public sealed class UnderstandKitSentenceValidator : AbstractValidator<UnderstandKitSentenceQuery>
{
    public UnderstandKitSentenceValidator()
    {
        RuleFor(query => query.Sentence).NotEmpty().MaximumLength(UnderstandKitSentenceQuery.MaxLength);
    }
}

/// <summary>
/// A sentence turned into a kit's form. **The model chooses only the kit**,
/// among the shop's own, as a closed question in plain text (`ClosedQuestion`;
/// ADR 0055). Code reads the budget and the requirements, because they are
/// written in the sentence (invariant 12), and a requirement the chosen kit
/// does not offer is not filled in. The kits are read through Search's
/// `list_kits` (ADR 0054), because Assist may not reference Search.
///
/// It runs on the storefront's model, Tino's profile: one local model on the
/// shopper's side, and so one place that configures it (`TinoOptions`, read
/// here and by the status endpoint through `HasModel`). Its own section would
/// be a second switch that could disagree with Tino's about the same model. With Tino off, no profile, or a model that does not answer,
/// nothing is understood, and the page keeps its form (invariant 8).
/// </summary>
public sealed class UnderstandKitSentenceHandler(
    IEnumerable<IReadTool> reads,
    IServiceProvider services,
    ModelProfiles profiles,
    IOptions<TinoOptions> tino,
    ILogger<UnderstandKitSentenceHandler> logger) : IQueryHandler<UnderstandKitSentenceQuery, KitSentenceUnderstood>
{
    /// <summary>What the call is metered under.</summary>
    public const string Operation = "assist.kit_sentence";

    /// <summary>The read it needs, by the name Search gives it.</summary>
    public const string KitsRead = "list_kits";

    public const string PromptVersion = "kit-sentence-1";

    /// <summary>The option that says the sentence asks for none of the shop's kits.</summary>
    public const string NoneOfThese = "none of these";

    private static readonly ActivitySource Telemetry = new(TelemetrySources.Assist);

    public async Task<KitSentenceUnderstood> HandleAsync(UnderstandKitSentenceQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("assist.kit_sentence");
        activity?.SetTag("assist.culture", query.Culture);

        var settings = tino.Value;
        var candidates = settings.Enabled ? ProfileWalk.Find(services, profiles, settings.Model, settings.Fallback) : [];
        var kitsRead = reads.FirstOrDefault(read => read is { Audience: ToolAudience.Page, Name: KitsRead });
        if (candidates.Count == 0 || kitsRead is null)
        {
            activity?.SetTag("assist.kit_sentence.outcome", "no_model");
            return KitSentenceUnderstood.Not;
        }

        var kits = await KitsAsync(kitsRead, query.Culture, cancellationToken);
        var question =
            $"A shopper wrote this to ask a shop to put a kit together for them: \"{query.Sentence.Trim()}\". Which of the shop's kits is the shopper asking for? If it is none of them, answer {NoneOfThese}.";

        foreach (var (name, client, _) in candidates)
        {
            try
            {
                var choice = await ClosedQuestion.AskAsync(
                    client, [], question, [.. kits.Select(kit => kit.Name), NoneOfThese],
                    ModelCall.Options(Operation, promptVersion: PromptVersion), cancellationToken);

                var kit = kits.FirstOrDefault(kit => string.Equals(kit.Name, choice.Option, StringComparison.OrdinalIgnoreCase));
                activity?.SetTag("assist.kit_sentence.outcome", kit is null ? "no_kit" : "understood");
                activity?.SetTag("assist.kit_sentence.template", kit?.Code);
                activity?.SetTag("assist.kit_sentence.confidence", choice.Confidence);
                if (kit is null)
                    return KitSentenceUnderstood.Not;

                var budget = KitSentenceReading.BudgetIn(query.Sentence);
                return new KitSentenceUnderstood(
                    kit.Code,
                    budget is { } amount ? new Money(amount, ShopOptions.DefaultCurrency) : null,
                    [.. kit.Requirements.Where(option => KitSentenceReading.Mentions(query.Sentence, option.Label)).Select(option => option.Facet)],
                    true);
            }
            catch (Exception failure) when (ModelFailures.IsModelFailure(failure) && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(failure, "The profile {Profile} did not say which kit a sentence asks for.", name);
            }
        }

        activity?.SetTag("assist.kit_sentence.outcome", "model_failed");
        return KitSentenceUnderstood.Not;
    }

    /// <summary>The shop's kits as `list_kits` answers them, read by their JSON: Assist has none of Search's types.</summary>
    private static async Task<IReadOnlyList<KitRead>> KitsAsync(IReadTool read, string culture, CancellationToken cancellationToken)
    {
        var answer = await read.ReadAsync(JsonSerializer.SerializeToElement(new { culture }, ArgumentSchemas.Json), cancellationToken);
        var json = JsonSerializer.SerializeToElement(answer.Data, answer.Data.GetType(), ArgumentSchemas.Json);
        return json.Deserialize<KitsOnSaleRead>(ArgumentSchemas.Json)?.Kits ?? [];
    }

    private sealed record KitsOnSaleRead(IReadOnlyList<KitRead> Kits);

    private sealed record KitRead(string Code, string Name, IReadOnlyList<RequirementRead> Requirements);

    private sealed record RequirementRead(string Facet, string Label);
}
