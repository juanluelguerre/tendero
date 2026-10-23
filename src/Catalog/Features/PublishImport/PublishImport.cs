using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.PublishImport;

/// <summary>
/// Publishes what one catalogue import brought, in one operation (phase 16,
/// P16-12; ADR 0012, amended 2026-10-23). The import still never publishes: a
/// person reads its answer — the counts and the notes — and then publishes the
/// run, rather than a hundred and twenty products one by one.
/// </summary>
public sealed record PublishImportCommand(ImportRunId ImportId) : ICommand<PublishImportResult>;

public enum PublishImportOutcome
{
    Published,
    NotFound,
}

/// <param name="Published">Moved from Draft to Active.</param>
/// <param name="AlreadyActive">On sale already: nothing to do.</param>
/// <param name="Withdrawn">In draft because a person withdrew it after it was on sale: left there.</param>
/// <param name="Archived">Out of the catalogue: left there.</param>
public sealed record PublishImportResult(
    PublishImportOutcome Outcome, int Published, int AlreadyActive, int Withdrawn, int Archived) : IAuditedResult
{
    AuditOutcome IAuditedResult.AuditOutcome =>
        Outcome == PublishImportOutcome.Published ? AuditOutcome.Allowed : AuditOutcome.Denied;

    string? IAuditedResult.AuditReason =>
        Outcome == PublishImportOutcome.Published
            ? $"published {Published}, already active {AlreadyActive}, withdrawn {Withdrawn}, archived {Archived}"
            : Outcome.ToString();
}

/// <summary>What the answer says: how many of the run's products each way.</summary>
public sealed record PublishImportResponse(int Published, int AlreadyActive, int Withdrawn, int Archived);

public sealed class PublishImportEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        // POST /api/catalog/imports/{id}/publish — the id the import answered with.
        app.MapPost("/api/catalog/imports/{id:guid}/publish",
                async Task<Results<Ok<PublishImportResponse>, NotFound>> (
                    Guid id, ICommandDispatcher dispatcher, CancellationToken ct) =>
                {
                    var result = await dispatcher.SendAsync(new PublishImportCommand(new ImportRunId(id)), ct);

                    return result.Outcome switch
                    {
                        PublishImportOutcome.NotFound => TypedResults.NotFound(),
                        PublishImportOutcome.Published => TypedResults.Ok(
                            new PublishImportResponse(result.Published, result.AlreadyActive, result.Withdrawn, result.Archived)),
                    };
                })
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("PublishImport");
    }
}

/// <summary>
/// Each product the run created, published unless someone has decided on it
/// since: on sale already, withdrawn after being on sale (a draft a person put
/// back), or archived. One save, so the run is published whole or not at all,
/// and each publication raises its own <c>ProductUpserted</c> for the index.
///
/// The products are read one by one: 120 reads at laboratory scale, and the
/// batch read waits for a catalogue where the number matters (the scale rule).
/// </summary>
public sealed class PublishImportHandler(
    IImportRunRepository runs,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    IActorAccessor actors)
    : ICommandHandler<PublishImportCommand, PublishImportResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);

    public async Task<PublishImportResult> HandleAsync(PublishImportCommand command, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.publish_import");
        activity?.SetTag("catalog.import_id", command.ImportId.Value);

        if (await runs.FindAsync(command.ImportId, cancellationToken) is not { } run)
            return new PublishImportResult(PublishImportOutcome.NotFound, 0, 0, 0, 0);

        activity?.SetTag("catalog.source", run.Source);
        int published = 0, alreadyActive = 0, withdrawn = 0, archived = 0;

        foreach (var id in run.Products)
        {
            switch (await products.FindByIdAsync(id, cancellationToken))
            {
                // The catalogue archives and never deletes, so a row that is gone
                // is out of it the way an archived one is.
                case null or { Status: ProductStatus.Archived }:
                    archived++;
                    break;
                case { Status: ProductStatus.Active }:
                    alreadyActive++;
                    break;
                case { FirstPublishedAt: not null }:
                    withdrawn++;
                    break;
                case { } draft:
                    draft.Publish(clock);
                    published++;
                    break;
            }
        }

        if (published > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            ProductsPublished.Record(actors.Current, published);
        }

        activity?.SetTag("catalog.published", published);
        return new PublishImportResult(PublishImportOutcome.Published, published, alreadyActive, withdrawn, archived);
    }
}
