using ElGuerre.Tendero.Accounts.Domain;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Accounts.Adapters;

/// <summary>A rehearsal's customers, in memory for one scope: registered scoped, so they die with the request.</summary>
internal sealed class RehearsedCustomers : IRehearsedCustomers
{
    private readonly Dictionary<CustomerOrigin, CustomerId> brought = [];

    public void Bring(CustomerOrigin origin) => this.brought.TryAdd(origin, CustomerId.New());

    public IReadOnlyDictionary<string, CustomerId> Of(string source) =>
        this.brought
            .Where(entry => entry.Key.Source == source)
            .ToDictionary(entry => entry.Key.ExternalId, entry => entry.Value, StringComparer.Ordinal);
}
