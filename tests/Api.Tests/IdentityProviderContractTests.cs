using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// What any identity provider has to honour for Tendero to work with it.
/// Abstract on purpose: the development issuer inherits it today, and Keycloak
/// will inherit it when it arrives, **without changing a line**.
///
/// That is the point of identity being a port (ADR 0003 applied to an external
/// system that is not ours). On the day of the swap, "can it be replaced?" has an
/// executable answer instead of an opinion — and a Keycloak that does not pass
/// this is a Keycloak that would have broken production.
///
/// The contract is not a C# interface: it is OIDC. Discovery, JWKS, and a token
/// the API validates.
/// </summary>
public abstract class IdentityProviderContractTests : IDisposable
{
    private readonly TenderoApiFactory _factory = new();

    protected HttpClient Client { get; }

    /// <summary>Ruta base del emisor bajo prueba.</summary>
    protected abstract string IssuerPath { get; }

    /// <summary>An identity with the shopkeeper role that the issuer can sign for.</summary>
    protected abstract string ShopkeeperSubject { get; }

    protected IdentityProviderContractTests() => Client = _factory.CreateClient();

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task It_publishes_a_discovery_document()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync($"{IssuerPath}/.well-known/openid-configuration", ct);
        response.EnsureSuccessStatusCode();

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;

        // With no issuer and no jwks_uri, JwtBearer cannot even begin to validate.
        Assert.False(string.IsNullOrWhiteSpace(document["issuer"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(document["jwks_uri"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(document["token_endpoint"]?.GetValue<string>()));
    }

    [Fact]
    public async Task It_publishes_a_signing_key()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync($"{IssuerPath}/.well-known/jwks.json", ct);
        response.EnsureSuccessStatusCode();

        var keys = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!["keys"]!.AsArray();

        Assert.NotEmpty(keys);
        // `kid` is what makes rotation possible: without it, changing keys
        // invalidates every token in flight instead of overlapping.
        Assert.All(keys, key => Assert.False(string.IsNullOrWhiteSpace(key!["kid"]?.GetValue<string>())));
    }

    [Fact]
    public async Task A_token_it_signs_opens_a_protected_endpoint()
    {
        var ct = TestContext.Current.CancellationToken;

        var token = await GetTokenAsync(ShopkeeperSubject, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/search/reindex");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request, ct);

        // It does not check for a 200: reindexing needs Postgres and
        // Elasticsearch, and there is neither here. What is checked is that
        // AUTHORIZATION passed, which is the only thing this contract measures.
        //
        // The message carries the WWW-Authenticate header because that is where
        // JwtBearer says WHY it refused — signature, issuer, audience or expiry —
        // and without it one 401 is indistinguishable from another.
        var reason = response.Headers.WwwAuthenticate.ToString();
        Assert.True(
            response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
            $"The issuer's own token was rejected: {(int)response.StatusCode} {response.StatusCode}. {reason}");
    }

    [Fact]
    public async Task Without_a_token_a_protected_endpoint_answers_401()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await Client.PostAsync("/api/search/reindex", content: null, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;

        var token = await GetTokenAsync(ShopkeeperSubject, ct);

        // Change one character of the signature: the payload is still valid and
        // the signature is not. It is the failure ValidateIssuerSigningKey exists
        // to catch, and the one a badly built fake issuer would let through.
        var tampered = token[..^2] + (token[^2] == 'A' ? "B" : "A") + token[^1];

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/search/reindex");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        var response = await Client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Public_endpoints_stay_reachable_without_a_token()
    {
        var ct = TestContext.Current.CancellationToken;

        // The catalogue is public by a written decision, not by an oversight. If
        // somebody put a policy here, this would say so before a user did.
        var response = await Client.GetAsync("/api/catalog/products", ct);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>How a token is asked of THIS issuer.</summary>
    protected abstract Task<string> GetTokenAsync(string subject, CancellationToken cancellationToken);
}

/// <summary>
/// The development issuer, honouring the contract. Three lines, which is what
/// adding an adapter should cost when the suite is well written — exactly like
/// <c>SeedCatalogConnectorContractTests</c>.
/// </summary>
public sealed class DevIssuerContractTests : IdentityProviderContractTests
{
    protected override string IssuerPath => "/dev-issuer";
    protected override string ShopkeeperSubject => "juanlu";

    protected override async Task<string> GetTokenAsync(string subject, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(
            [new("grant_type", "password"), new("username", subject)]);

        var response = await Client.PostAsync($"{IssuerPath}/connect/token", form, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!;
        return body["accessToken"]!.GetValue<string>();
    }
}
