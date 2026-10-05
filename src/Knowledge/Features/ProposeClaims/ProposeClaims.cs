using System.Diagnostics;
using System.Diagnostics.Metrics;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.AI.Support;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Knowledge.Features.ProposeClaims;

/// <summary>Read a product's current description and propose what it states (ADR 0018).</summary>
public sealed record ProposeClaimsCommand(ProductId ProductId) : ICommand<ProposeClaimsResult>;

public sealed record ProposeClaimsResult(int Proposed, IReadOnlyList<DropReason> Dropped, string Extractor);

/// <summary>
/// The slow half: the model reads the description, code vets what it proposed
/// (the quote is in the text, the value parses, each statement is in its
/// language), the support port scores each one against the text, and what is
/// left waits for a person. Run off the outbox, one product at a time.
///
/// The model when the AppHost started it, the rules otherwise — the path with no
/// model (invariant 8). A rule's claim is the text's own words, so with no scorer
/// it is supported by construction; a model's claim is never proposed unscored.
/// </summary>
public sealed class ProposeClaimsHandler(
    IKnowledgeRepository knowledge,
    IAttributeVocabulary vocabulary,
    IServiceProvider services,
    ExtractorModel extractorModel,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ProposeClaimsCommand, ProposeClaimsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Knowledge);

    public async Task<ProposeClaimsResult> HandleAsync(ProposeClaimsCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("knowledge.propose_claims");
        activity?.SetTag("product.id", command.ProductId.ToString());

        var documents = await knowledge.CurrentDocumentsAsync(command.ProductId, cancellationToken);
        var spanish = documents.FirstOrDefault(document => document.Culture == "es");
        var english = documents.FirstOrDefault(document => document.Culture == "en");
        if (spanish is null || !spanish.IsWaiting)
            return new ProposeClaimsResult(0, [], "none");

        var scorer = services.GetService<ISupportScorer>();
        var model = scorer is null ? null : services.GetKeyedService<IClaimExtractor>(ClaimExtractors.Chat);
        var (extractor, name) = model is null
            ? (services.GetRequiredKeyedService<IClaimExtractor>(ClaimExtractors.Rules), ClaimExtractors.Rules)
            : (model, ClaimExtractors.Chat);
        activity?.SetTag("knowledge.extractor", name);

        var attributes = await vocabulary.CurrentAsync(cancellationToken);
        var proposed = await extractor.ExtractAsync(new ProductText(spanish.Text, english?.Text ?? ""), attributes, cancellationToken);
        var (kept, dropped) = ClaimVetting.Vet(proposed, spanish, english, attributes);

        var provenance = name == ClaimExtractors.Chat
            ? new Provenance(name, extractorModel.ModelId, extractorModel.Revision, ChatClaimExtractor.PromptVersion, Guid.CreateVersion7().ToString(), clock.GetUtcNow())
            : new Provenance(name, null, null, null, Guid.CreateVersion7().ToString(), clock.GetUtcNow());

        foreach (var claim in kept)
        {
            var support = scorer is null ? 1.0 : await scorer.ScoreAsync(spanish.Text, claim.Statement.In("es"), cancellationToken);
            List<Evidence> evidence = [new(spanish.Id, claim.QuoteSpanish, support)];
            if (english is not null && claim.QuoteEnglish is { } quoteEnglish)
                evidence.Add(new Evidence(english.Id, quoteEnglish, support));

            knowledge.Add(ProductClaim.Propose(command.ProductId, claim.Statement, claim.AttributeCode, claim.Value, evidence, provenance, clock));
        }

        spanish.MarkProposed(clock);
        english?.MarkProposed(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        ClaimsProposed.Record(name, kept.Count, dropped);
        activity?.SetTag("knowledge.claims.proposed", kept.Count);
        activity?.SetTag("knowledge.claims.dropped", dropped.Count);
        return new ProposeClaimsResult(kept.Count, dropped, name);
    }
}

/// <summary>
/// `tendero.knowledge.claims.proposed` and `tendero.knowledge.claims.dropped` (the
/// tracking plan): how much of what the model proposes reaches a person, and why
/// the rest does not — the numbers that say whether the extractor, its prompt or
/// its settings need to change.
/// </summary>
internal static class ClaimsProposed
{
    private static readonly Meter Meter = new(TelemetrySources.Knowledge);

    private static readonly Counter<long> Proposed = Meter.CreateCounter<long>(
        "tendero.knowledge.claims.proposed", unit: "{claim}", description: "Claims proposed for review, by the extractor that read the text.");

    private static readonly Counter<long> Dropped = Meter.CreateCounter<long>(
        "tendero.knowledge.claims.dropped", unit: "{claim}", description: "Claims an extractor proposed that code refused before any person saw them, by reason.");

    public static void Record(string extractor, int proposed, IReadOnlyList<DropReason> dropped)
    {
        Proposed.Add(proposed, new KeyValuePair<string, object?>("extractor", extractor));
        foreach (var reason in dropped)
            Dropped.Add(1, new KeyValuePair<string, object?>("reason", MetricLabels.Of(reason)));
    }
}
