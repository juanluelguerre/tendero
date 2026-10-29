using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Domain;

public readonly record struct ImportRunId(Guid Value)
{
    public static ImportRunId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// One run of a catalogue import, and the products it created (phase 16,
/// P16-12): what "what one migration brought" means when a person publishes it
/// at once. Only the products it created — a product it updated was already
/// the shop's, and its status someone else's decision.
/// </summary>
public sealed class ImportRun
{
    private readonly List<ProductId> products = [];

    private ImportRun() { } // EF Core

    public ImportRunId Id { get; private set; }

    public string Source { get; private set; } = default!;

    public DateTimeOffset StartedAt { get; private set; }

    public IReadOnlyList<ProductId> Products => this.products;

    public static ImportRun Start(string source, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return new ImportRun { Id = ImportRunId.New(), Source = source.ToLowerInvariant(), StartedAt = at };
    }

    /// <summary>The run created this product.</summary>
    public void Brought(ProductId product)
    {
        if (!this.products.Contains(product))
            this.products.Add(product);
    }
}
