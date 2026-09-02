using ElGuerre.Tendero.Catalog.Domain;

namespace ElGuerre.Tendero.Catalog.Ports;

/// <summary>
/// The whole category tree. The index projection needs it (to render the branch
/// in each culture) and so does the backoffice; with two consumers and a third
/// in sight, it is a port and not a loose query.
/// </summary>
public interface ICategoryReader
{
    Task<CategoryTree> AllAsync(CancellationToken cancellationToken = default);
}
