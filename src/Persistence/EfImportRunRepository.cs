using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;

namespace ElGuerre.Tendero.Persistence;

/// <summary>The catalogue's import runs (P16-12), tracked: the import adds to its run as batches are saved.</summary>
internal sealed class EfImportRunRepository(TenderoDbContext context) : IImportRunRepository
{
    public void Add(ImportRun run) => context.ImportRuns.Add(run);

    public async Task<ImportRun?> FindAsync(ImportRunId id, CancellationToken ct) =>
        await context.ImportRuns.FindAsync([id], ct);
}
