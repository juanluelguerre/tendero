using ElGuerre.Tendero.Catalog.Domain;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// The catalogue's import runs (phase 16, P16-12): the import records one, the
/// bulk publication reads it. Two slices, so a port of its own.
/// </summary>
public interface IImportRunRepository
{
    void Add(ImportRun run);

    Task<ImportRun?> FindAsync(ImportRunId id, CancellationToken ct);
}
