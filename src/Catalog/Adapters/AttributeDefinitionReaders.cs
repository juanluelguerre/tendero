using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Catalog.Adapters;

/// <summary>
/// The definitions the repository ships, cached for the process.
///
/// It is the default adapter and the one the quality gate uses, which
/// deliberately never touches Postgres. It caches because the index projection
/// asks for them once per product per culture: without a cache, reindexing six
/// products would read the file twenty-four times.
///
/// When the backoffice lets them be edited, the adapter that reads from Postgres
/// registers in its place and nothing else changes — which is the reason this is
/// a port at all.
/// </summary>
internal sealed class SeedFileAttributeDefinitionReader(
    IOptions<AttributeSeedOptions> options, TimeProvider clock) : IAttributeDefinitionReader
{
    private AttributeDefinitions? cached;

    public async Task<AttributeDefinitions> AllAsync(CancellationToken cancellationToken = default)
    {
        if (this.cached is not null)
            return this.cached;

        var path = options.Value.FilePath;

        // With no file it carries on: a catalogue with no definitions indexes
        // attributes as plain text, which is the previous behaviour. Failing
        // here would turn missing data into a shop that is down.
        if (!File.Exists(path))
            return this.cached = AttributeDefinitions.Empty;

        return this.cached = new AttributeDefinitions(
            await AttributeDefinitionSeedFile.LoadAsync(path, clock, cancellationToken));
    }
}
