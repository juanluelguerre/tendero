using ElGuerre.Tendero.Knowledge.Domain;
using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Features.RecordDescription;

/// <summary>What a product's description says now, in one culture.</summary>
public sealed record RecordDescriptionCommand(ProductId ProductId, string Culture, string Text) : ICommand<RecordDescriptionResult>;

public enum RecordDescriptionOutcome
{
    /// <summary>A text never seen for this product: stored, and waiting for claims.</summary>
    Recorded,

    /// <summary>The same text as the current document: nothing to do. The hash makes a replayed event harmless.</summary>
    Unchanged,
}

public sealed record RecordDescriptionResult(RecordDescriptionOutcome Outcome, int Superseded) : IAuditedResult
{
    /// <summary>Neither outcome is a refusal: an unchanged text is simply nothing to record.</summary>
    AuditOutcome IAuditedResult.AuditOutcome => Outcome switch
    {
        RecordDescriptionOutcome.Recorded or RecordDescriptionOutcome.Unchanged => AuditOutcome.Allowed,
    };

    string? IAuditedResult.AuditReason => null;
}

/// <summary>
/// The fast half of proposing claims (ADR 0018), and the only half the outbox
/// runs: a text is stored by its hash, and the claims resting on the text it
/// replaces are superseded. Milliseconds — the model runs later, off the outbox,
/// because thirty seconds a product would hold every projection behind it.
/// </summary>
public sealed class RecordDescriptionHandler(IKnowledgeRepository knowledge, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RecordDescriptionCommand, RecordDescriptionResult>
{
    public async Task<RecordDescriptionResult> HandleAsync(RecordDescriptionCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Text))
            return new RecordDescriptionResult(RecordDescriptionOutcome.Unchanged, 0);

        var id = SourceDocumentId.Of(command.ProductId, command.Culture, command.Text);
        var current = (await knowledge.CurrentDocumentsAsync(command.ProductId, cancellationToken))
            .FirstOrDefault(document => document.Culture == command.Culture);

        if (current?.Id == id)
            return new RecordDescriptionResult(RecordDescriptionOutcome.Unchanged, 0);

        // A text this product had before (A → B → A) is reinstated, not stored twice.
        if (await knowledge.FindDocumentAsync(id, cancellationToken) is { } earlier)
            earlier.Reinstate(clock);
        else
            knowledge.Add(SourceDocument.Describe(command.ProductId, command.Culture, command.Text, clock));

        // What rested on the text this one replaces no longer stands. An approved
        // claim superseded here says so in its event, so what it applied is revisited.
        var superseded = 0;
        if (current is not null)
        {
            foreach (var claim in await knowledge.StandingClaimsAsync(command.ProductId, cancellationToken))
            {
                if (claim.Evidence.Any(evidence => evidence.Document == current.Id))
                {
                    claim.Supersede(clock);
                    superseded++;
                }
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new RecordDescriptionResult(RecordDescriptionOutcome.Recorded, superseded);
    }
}
