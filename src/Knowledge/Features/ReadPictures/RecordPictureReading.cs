using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Features.ReadPictures;

/// <summary>What a model wrote about one picture of a product, in both languages.</summary>
public sealed record RecordPictureReadingCommand(ProductId ProductId, PictureReading Reading, string Spanish, string English)
    : ICommand<RecordPictureReadingResult>;

public enum RecordPictureReadingOutcome
{
    /// <summary>Words never read in this picture: stored, and waiting for claims.</summary>
    Recorded,

    /// <summary>The picture read again to the same words, or to none: nothing to do.</summary>
    Unchanged,
}

public sealed record RecordPictureReadingResult(RecordPictureReadingOutcome Outcome, int Superseded) : IAuditedResult
{
    /// <summary>Neither outcome is a refusal: the same words again are simply nothing to record.</summary>
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        RecordPictureReadingOutcome.Recorded or RecordPictureReadingOutcome.Unchanged => AuditOutcome.Allowed,
    };

    string? IAuditedResult.AuditReason => null;
}

/// <summary>
/// The fast half of reading a picture (phase 13a, I-4; ADR 0018): the words a
/// model wrote about it are stored by what they are — the product, the picture,
/// the language and the text — and the claims resting on the words they replace
/// are superseded. No model is called here; the one that read the picture ran
/// before, off any request.
///
/// A reading is kept in both languages or not at all: the extraction that
/// follows reads the two, as it does a description's.
/// </summary>
public sealed class RecordPictureReadingHandler(IKnowledgeRepository knowledge, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RecordPictureReadingCommand, RecordPictureReadingResult>
{
    public async Task<RecordPictureReadingResult> HandleAsync(RecordPictureReadingCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Spanish) || string.IsNullOrWhiteSpace(command.English))
            return new RecordPictureReadingResult(RecordPictureReadingOutcome.Unchanged, 0);

        var current = await knowledge.CurrentReadingsAsync(command.ProductId, command.Reading.Picture, cancellationToken);
        List<SourceDocumentId> replaced = [];

        (string Culture, string Text)[] words = [("es", command.Spanish), ("en", command.English)];

        foreach (var (culture, text) in words)
        {
            var id = SourceDocumentId.OfPicture(command.ProductId, culture, text, command.Reading.Picture);
            var before = current.FirstOrDefault(document => document.Culture == culture);

            if (before?.Id == id)
                continue;

            // Words this picture was read to before (A → B → A) are reinstated, not stored twice.
            if (await knowledge.FindDocumentAsync(id, cancellationToken) is { } earlier)
                earlier.Reinstate(clock);
            else
                knowledge.Add(SourceDocument.Depict(command.ProductId, culture, text, command.Reading, clock));

            if (before is not null)
                replaced.Add(before.Id);
        }

        if (replaced.Count == 0 && current.Count == 2)
            return new RecordPictureReadingResult(RecordPictureReadingOutcome.Unchanged, 0);

        // What rested on the words these replace no longer stands.
        var superseded = 0;
        if (replaced.Count > 0)
        {
            foreach (var claim in await knowledge.StandingClaimsAsync(command.ProductId, cancellationToken))
            {
                if (claim.Evidence.Any(evidence => replaced.Contains(evidence.Document)))
                {
                    claim.Supersede(clock);
                    superseded++;
                }
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RecordPictureReadingResult(RecordPictureReadingOutcome.Recorded, superseded);
    }
}
