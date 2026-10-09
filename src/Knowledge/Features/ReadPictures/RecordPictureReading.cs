using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Extraction;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Features.ReadPictures;

/// <summary>What a model wrote about one picture of a product, in both languages, and what it answered.</summary>
/// <param name="Count">How many items it counted in the picture, or null when the picture was not asked.</param>
/// <param name="CountConfidence">The probability the model gave that number, when its server said.</param>
/// <param name="Colour">The colour it chose, beside the one the picture is held to — or null when the picture was not asked.</param>
public sealed record RecordPictureReadingCommand(
    ProductId ProductId, PictureReading Reading, string Spanish, string English, int? Count, double? CountConfidence = null,
    ColourSeen? Colour = null)
    : ICommand<RecordPictureReadingResult>;

public enum RecordPictureReadingOutcome
{
    /// <summary>Words never read in this picture: stored, and waiting for claims.</summary>
    Recorded,

    /// <summary>The picture read again to the same words, or to none: nothing to do.</summary>
    Unchanged,
}

/// <param name="DoubtRaised">Whether the reading left a doubt about the picture for a person to look at.</param>
public sealed record RecordPictureReadingResult(RecordPictureReadingOutcome Outcome, int Superseded, bool DoubtRaised = false) : IAuditedResult
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
/// A reading is kept in both languages or not at all: the claim that follows
/// quotes the two, as a description's does.
///
/// **What the picture answered is written under the words**
/// (<see cref="PictureAnswers"/>): the count is a line of the document, so it is
/// in the document's identity — the same words with another count are another
/// reading, and what rested on the old one falls.
///
/// **And a colour that differs is a doubt, raised here** (I-4d): when the model
/// is sure the picture shows another colour than the variants it is of hold. A
/// doubt the new reading no longer says is withdrawn, and one a person already
/// decided is not put to them again.
/// </summary>
public sealed class RecordPictureReadingHandler(
    IKnowledgeRepository knowledge, IPictureDoubtRepository doubts, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RecordPictureReadingCommand, RecordPictureReadingResult>
{
    public async Task<RecordPictureReadingResult> HandleAsync(RecordPictureReadingCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Spanish) || string.IsNullOrWhiteSpace(command.English))
            return new RecordPictureReadingResult(RecordPictureReadingOutcome.Unchanged, 0);

        var current = await knowledge.CurrentReadingsAsync(command.ProductId, command.Reading.Picture, cancellationToken);
        List<SourceDocumentId> replaced = [];

        // A picture that was not asked how many has only its words.
        (string Culture, string Text)[] words = command.Count is { } count
            ?
            [
                ("es", PictureAnswers.Under(command.Spanish, "es", count, command.CountConfidence)),
                ("en", PictureAnswers.Under(command.English, "en", count, command.CountConfidence)),
            ]
            : [("es", command.Spanish.TrimEnd()), ("en", command.English.TrimEnd())];

        if (command.Colour is { } seen)
            words = [.. words.Select(word => (word.Culture, PictureAnswers.WithColour(word.Text, word.Culture, seen.SeenLabel.In(word.Culture), seen.Confidence)))];

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

        var raised = await DoubtAsync(command, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RecordPictureReadingResult(RecordPictureReadingOutcome.Recorded, superseded, raised);
    }

    /// <summary>
    /// What the new reading means for the picture's doubts: an open one it no
    /// longer says is withdrawn, and the one it does say is raised — unless it is
    /// already open, or a person has already answered it.
    /// </summary>
    private async Task<bool> DoubtAsync(RecordPictureReadingCommand command, CancellationToken cancellationToken)
    {
        var raised = command.Colour is null ? null : PictureDoubt.Raise(command.ProductId, command.Reading, command.Colour, clock);
        var before = await doubts.DoubtsAboutAsync(command.ProductId, command.Reading.Picture, cancellationToken);

        foreach (var open in before.Where(doubt => doubt.Status == PictureDoubtStatus.Open))
        {
            if (raised is null || !open.IsAbout(command.Colour!))
                open.Withdraw(clock);
        }

        if (raised is null
            || before.Any(doubt => doubt.Status is PictureDoubtStatus.Open or PictureDoubtStatus.Decided && doubt.IsAbout(command.Colour!)))
        {
            return false;
        }

        doubts.Add(raised);
        return true;
    }
}
