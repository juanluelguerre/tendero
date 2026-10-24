using ElGuerre.Tendero.Accounts.Ports;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests.Identity;

/// <summary>
/// The provisioning port's executable contract (phase 16, S2b; ADR 0067): every
/// adapter — Keycloak's admin API, the development issuer — passes the same suite.
/// Linked as a file into each test project that has one.
/// </summary>
public abstract class IdentityProvisionerContractTests
{
    protected abstract IIdentityProvisioner CreateProvisioner();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IdentityToProvision Person(string email) => new(email, "Ana", "Ruiz", "es");

    [Fact]
    public async Task Provisioning_answers_with_the_identitys_subject()
    {
        var subject = await CreateProvisioner().ProvisionAsync(Person("ana@example.com"), Ct);

        Assert.False(string.IsNullOrWhiteSpace(subject));
    }

    /// <summary>The same person twice is one identity: how an import recognises somebody the shop already had.</summary>
    [Fact]
    public async Task The_same_email_twice_is_one_identity_whatever_its_case()
    {
        var provisioner = CreateProvisioner();

        var first = await provisioner.ProvisionAsync(Person("ana@example.com"), Ct);
        var again = await provisioner.ProvisionAsync(Person("Ana@Example.com"), Ct);

        Assert.Equal(first, again);
    }

    [Fact]
    public async Task Two_people_are_two_identities()
    {
        var provisioner = CreateProvisioner();

        var ana = await provisioner.ProvisionAsync(Person("ana@example.com"), Ct);
        var luis = await provisioner.ProvisionAsync(Person("luis@example.com"), Ct);

        Assert.NotEqual(ana, luis);
    }
}
