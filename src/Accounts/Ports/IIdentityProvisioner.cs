using ElGuerre.Tendero.Accounts.Domain;

namespace ElGuerre.Tendero.Accounts.Ports;

/// <summary>
/// Makes an identity at the identity provider for somebody the shop already knows
/// (phase 16, S2b, P16-17; ADR 0067): an imported customer, with their email and
/// no password, who sets one through the provider's own *forgot password* — which
/// is also what proves the address is theirs.
///
/// It creates identities and never authenticates them, so invariant 10 holds: the
/// shop still only validates tokens, and the provider is still the one that signs
/// people in. Adapters: Keycloak's admin API and the development issuer, held by
/// one contract suite.
/// </summary>
public interface IIdentityProvisioner
{
    /// <summary>
    /// The subject of the identity with this email: made now, or the one that
    /// already had it — so provisioning the same person twice makes one identity.
    /// </summary>
    Task<string> ProvisionAsync(IdentityToProvision person, CancellationToken cancellationToken = default);
}

public sealed record IdentityToProvision(string Email, string? FirstName, string? LastName, string? Culture);

/// <summary>
/// The question an import asks of the customers the shop already has (phase 16,
/// S2b): was this person imported before, from this source. A port of its own
/// rather than one more method on <see cref="ICustomerRepository"/>, which the
/// other slices use and which has no reason to learn about sources.
///
/// "Is this somebody the shop already knows" is not asked by email: a customer who
/// signed in here keeps no email, the identity provider does. It is asked through
/// the provisioner, which answers with the identity that already had the email,
/// and then by that subject.
/// </summary>
public interface IImportedCustomers
{
    Task<Customer?> FindByOriginAsync(CustomerOrigin origin, CancellationToken ct);
}
