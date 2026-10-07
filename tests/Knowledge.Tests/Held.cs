using ElGuerre.Tendero.Knowledge.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>What the catalogue holds, as a test says it does — and what it was asked.</summary>
public sealed class Held(Dictionary<ProductId, IReadOnlyList<HeldValue>> values) : ICatalogueValues
{
    /// <summary>A catalogue that holds nothing of any product: for the tests that are about something else.</summary>
    public static Held Nothing => new([]);

    public List<IReadOnlyCollection<ProductId>> Asked { get; } = [];

    public Task<IReadOnlyDictionary<ProductId, IReadOnlyList<HeldValue>>> OfAsync(
        IReadOnlyCollection<ProductId> products, string culture, CancellationToken cancellationToken = default)
    {
        Asked.Add(products);
        return Task.FromResult<IReadOnlyDictionary<ProductId, IReadOnlyList<HeldValue>>>(
            values.Where(held => products.Contains(held.Key)).ToDictionary(held => held.Key, held => held.Value));
    }
}
