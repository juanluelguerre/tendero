using System.Security.Claims;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The one class that knows claims exist, and the one every handler trusts to
/// say who is acting.
///
/// It had no tests of its own until the segment had to travel through it: the
/// endpoint-policy tests prove a token is VALIDATED, and nothing proved what the
/// domain is told about the person behind it. These drive the accessor the API
/// actually registers, with only <see cref="ICustomerDirectory"/> replaced — so
/// the DI wiring is under test too, not a hand-built instance of the class.
/// </summary>
public sealed class PrincipalAccessorTests
{
    // ---------- Who is acting ----------

    [Fact]
    public void A_request_with_nobody_signed_in_is_the_anonymous_principal()
    {
        var principal = Resolve(new ClaimsPrincipal(new ClaimsIdentity()), new FakeDirectory());

        Assert.Same(CommercePrincipal.Anonymous, principal);
    }

    /// <summary>
    /// Signed in, and never registered: the token names somebody the shop has
    /// no row for. The accessor must answer with the subject and NO customer —
    /// it is a read, and creating an account on first sight would make a GET a
    /// write with no audit entry.
    /// </summary>
    [Fact]
    public void Somebody_accounts_has_never_seen_has_a_subject_and_no_customer()
    {
        var principal = Resolve(SignedIn(new Claim("sub", "nuevo")), new FakeDirectory());

        Assert.Equal("nuevo", principal.Subject);
        Assert.Null(principal.Customer);
        Assert.Null(principal.Segment);
    }

    /// <summary>
    /// JwtBearer maps `sub` to <see cref="ClaimTypes.NameIdentifier"/> by
    /// default, so that is the claim a real token arrives with; `sub` is the
    /// fallback for a principal built without the mapping.
    /// </summary>
    [Fact]
    public void The_subject_is_read_from_the_name_identifier_before_sub()
    {
        var principal = Resolve(
            SignedIn(
                new Claim(ClaimTypes.NameIdentifier, "681c3d75-9cc6-437d-a9c2-98d1d41ecf36"),
                new Claim("sub", "ignored")),
            new FakeDirectory());

        Assert.Equal("681c3d75-9cc6-437d-a9c2-98d1d41ecf36", principal.Subject);
    }

    /// <summary>An agent is a principal, not a customer: the `agent_id` claim
    /// is what tells the two apart.</summary>
    [Fact]
    public void An_agent_token_is_an_agent_principal()
    {
        var principal = Resolve(
            SignedIn(new Claim("sub", "claude-desktop"), new Claim("agent_id", "claude-desktop")),
            new FakeDirectory());

        Assert.True(principal.IsAgent);
        Assert.Equal(new AgentId("claude-desktop"), principal.Agent);
    }

    [Fact]
    public void A_person_is_not_an_agent()
    {
        var principal = Resolve(SignedIn(new Claim("sub", "marta")), new FakeDirectory());

        Assert.False(principal.IsAgent);
    }

    [Fact]
    public void Roles_come_from_the_role_claims()
    {
        var principal = Resolve(
            SignedIn(
                new Claim("sub", "juanlu"),
                new Claim(ClaimTypes.Role, "shopper"),
                new Claim(ClaimTypes.Role, "shopkeeper")),
            new FakeDirectory());

        Assert.True(principal.IsInRole("shopper"));
        Assert.True(principal.IsInRole("shopkeeper"));
        Assert.False(principal.IsInRole("agent"));
    }

    /// <summary>
    /// The two issuers disagree about names and both are right: Keycloak sends
    /// `preferred_username`, the development issuer sends `name`, and a token
    /// carrying neither still names somebody — its subject.
    /// </summary>
    [Theory]
    [InlineData("preferred_username", "marta.k")]
    [InlineData(ClaimTypes.Name, "Marta")]
    public void The_display_name_is_the_first_name_the_token_carries(string claim, string expected)
    {
        var principal = Resolve(
            SignedIn(new Claim("sub", "681c3d75"), new Claim(claim, expected)),
            new FakeDirectory());

        Assert.Equal(expected, principal.DisplayName);
    }

    [Fact]
    public void The_preferred_username_wins_over_the_name()
    {
        var principal = Resolve(
            SignedIn(
                new Claim("sub", "681c3d75"),
                new Claim(ClaimTypes.Name, "Marta"),
                new Claim("preferred_username", "marta.k")),
            new FakeDirectory());

        Assert.Equal("marta.k", principal.DisplayName);
    }

    [Fact]
    public void A_token_with_no_name_is_displayed_by_its_subject()
    {
        var principal = Resolve(SignedIn(new Claim("sub", "681c3d75")), new FakeDirectory());

        Assert.Equal("681c3d75", principal.DisplayName);
    }

    // ---------- What Accounts knows about them ----------

    [Fact]
    public void A_signed_in_customer_carries_the_segment_accounts_holds_for_them()
    {
        var marta = CustomerId.New();

        var principal = Resolve(
            SignedIn(new Claim("sub", "marta")),
            new FakeDirectory(("marta", new KnownCustomer(marta, "vip"))));

        Assert.Equal(marta, principal.Customer);
        Assert.Equal("vip", principal.Segment);
    }

    /// <summary>
    /// A handler may read <c>Current</c> several times — the pricing engine asks
    /// for the segment, the audit step asks who acted. Each read going to the
    /// database would be a round trip for an answer that cannot change inside
    /// one request.
    /// </summary>
    [Fact]
    public void Accounts_is_asked_once_per_request_however_often_the_principal_is_read()
    {
        var directory = new FakeDirectory(("marta", new KnownCustomer(CustomerId.New(), "retail")));

        using var factory = FactoryWith(directory);
        using var scope = factory.Services.CreateScope();
        SetUser(scope, SignedIn(new Claim("sub", "marta")));

        var accessor = scope.ServiceProvider.GetRequiredService<IPrincipalAccessor>();
        _ = accessor.Current;
        _ = accessor.Current;
        _ = accessor.Current;

        Assert.Equal(1, directory.Lookups);
    }

    // ---------- Plumbing ----------

    private static CommercePrincipal Resolve(ClaimsPrincipal user, FakeDirectory directory)
    {
        using var factory = FactoryWith(directory);
        using var scope = factory.Services.CreateScope();
        SetUser(scope, user);

        return scope.ServiceProvider.GetRequiredService<IPrincipalAccessor>().Current;
    }

    private static WebApplicationFactory<Program> FactoryWith(
        FakeDirectory directory) =>
        new TenderoApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<ICustomerDirectory>(_ => directory)));

    private static void SetUser(IServiceScope scope, ClaimsPrincipal user) =>
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { User = user };

    private static ClaimsPrincipal SignedIn(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType: "test"));

    private sealed class FakeDirectory(params (string Subject, KnownCustomer Customer)[] customers)
        : ICustomerDirectory
    {
        public IReadOnlyDictionary<string, KnownCustomer> Known { get; } =
            customers.ToDictionary(entry => entry.Subject, entry => entry.Customer);

        public int Lookups { get; private set; }

        public Task<KnownCustomer?> ForSubjectAsync(string subject, CancellationToken ct = default)
        {
            Lookups++;
            return Task.FromResult(Known.GetValueOrDefault(subject));
        }
    }
}
