using System.Diagnostics;
using System.Diagnostics.Metrics;
using ElGuerre.Tendero.AI.Support;
using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace ElGuerre.Tendero.Knowledge.Features.ProposeClaims;

/// <summary>
/// Read what waits of a product — its current description, and what a model read
/// in each of its pictures — and propose what each states (ADR 0018; phase 13a, I-4).
/// </summary>
public sealed record ProposeClaimsCommand(ProductId ProductId) : ICommand<ProposeClaimsResult>;

public sealed record ProposeClaimsResult(int Proposed, IReadOnlyList<DropReason> Dropped, string Extractor);

/// <summary>
/// The slow half: the model reads the description, code vets what it proposed
/// (the quote is in the text, the value parses, each statement is in its
/// language), the support port scores each one against the text, and what is
/// left waits for a person. Run off the outbox, one product at a time.
///
/// **A source at a time, and each by itself** (phase 13a, I-4). The description
/// is one source and each picture's reading another: the extractor is handed one
/// source's two languages and never two sources as one text, so a claim rests on
/// the documents it was read from — a reading's claim on the reading, which says
/// which picture and which model wrote it. Each source is saved when it is done:
/// one that brings the extractor down leaves the ones before it proposed, and is
/// the only one tried again.
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

        var sources = await WaitingSourcesAsync(command.ProductId, cancellationToken);
        if (sources.Count == 0)
            return new ProposeClaimsResult(0, [], "none");

        var scorer = services.GetService<ISupportScorer>();
        var model = scorer is null ? null : services.GetKeyedService<IClaimExtractor>(ClaimExtractors.Chat);
        var (extractor, name) = model is null
            ? (services.GetRequiredKeyedService<IClaimExtractor>(ClaimExtractors.Rules), ClaimExtractors.Rules)
            : (model, ClaimExtractors.Chat);
        activity?.SetTag("knowledge.extractor", name);

        var attributes = await vocabulary.CurrentAsync(cancellationToken);
        var provenance = name == ClaimExtractors.Chat
            ? new Provenance(name, extractorModel.ModelId, extractorModel.Revision, ChatClaimExtractor.PromptVersion, Guid.CreateVersion7().ToString(), clock.GetUtcNow())
            : new Provenance(name, null, null, null, Guid.CreateVersion7().ToString(), clock.GetUtcNow());

        var proposedInAll = 0;
        List<DropReason> droppedInAll = [];

        foreach (var (spanish, english) in sources)
        {
            var proposed = await extractor.ExtractAsync(new ProductText(spanish.Text, english?.Text ?? ""), attributes, cancellationToken);
            var (kept, dropped) = ClaimVetting.Vet(proposed, spanish, english, attributes);

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
            proposedInAll += kept.Count;
            droppedInAll.AddRange(dropped);
        }

        activity?.SetTag("knowledge.sources", sources.Count);
        activity?.SetTag("knowledge.claims.proposed", proposedInAll);
        activity?.SetTag("knowledge.claims.dropped", droppedInAll.Count);
        return new ProposeClaimsResult(proposedInAll, droppedInAll, name);
    }

    /// <summary>
    /// What waits of the product, a source at a time: its description first, then
    /// each picture's reading by the picture's id — the same order on every run.
    /// A source is its Spanish document and, when there is one, its English: it
    /// waits when the Spanish one does, as the queue says.
    /// </summary>
    private async Task<IReadOnlyList<(SourceDocument Spanish, SourceDocument? English)>> WaitingSourcesAsync(
        ProductId productId, CancellationToken cancellationToken)
    {
        var description = await knowledge.CurrentDocumentsAsync(productId, cancellationToken);
        var readings = (await knowledge.CurrentReadingsAsync(productId, cancellationToken))
            .GroupBy(document => document.PictureId, StringComparer.Ordinal)
            .OrderBy(picture => picture.Key, StringComparer.Ordinal);

        IEnumerable<IEnumerable<SourceDocument>> all = [description, .. readings];

        return
        [
            .. all
                .Select(source => source.ToList())
                .Select(source => (
                    Spanish: source.FirstOrDefault(document => document.Culture == "es"),
                    English: source.FirstOrDefault(document => document.Culture == "en")))
                .Where(source => source.Spanish is { IsWaiting: true })
                .Select(source => (source.Spanish!, source.English)),
        ];
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
