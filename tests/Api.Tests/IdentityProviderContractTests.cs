using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;
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
    private readonly TenderoApiFactory factory;
    private readonly HttpClient? external;

    /// <summary>The API under test, trusting the issuer under test.</summary>
    protected HttpClient Client { get; }

    /// <summary>
    /// The issuer's base URL, or null when it lives inside the API process.
    ///
    /// This is the property the SECOND adapter added, and its arrival is the
    /// point: everything below was written when the only issuer was in-process,
    /// so "it inherits without changing a line" held right up until something
    /// external inherited it. A contract suite with one implementation has not
    /// been tested as a contract.
    /// </summary>
    protected abstract string? Authority { get; }

    /// <summary>An identity with the shopkeeper role that the issuer can sign for.</summary>
    protected abstract string ShopkeeperSubject { get; }

    /// <summary>
    /// Why this issuer cannot be reached, or null when it can.
    ///
    /// It exists because an external issuer is a prerequisite rather than a
    /// fixture: this repository has twice decided not to start infrastructure
    /// from a test — `tools/SearchEval` takes an Elasticsearch URL, Playwright
    /// takes a running stack — because the alternative is a second, worse
    /// AppHost. A skip with a reason is the honest version of that.
    /// </summary>
    protected virtual string? Unavailable => null;

    /// <summary>Where the discovery document is. The one path OIDC guarantees.</summary>
    private string DiscoveryUrl =>
        $"{Authority ?? "/dev-issuer"}/.well-known/openid-configuration";

    /// <summary>Whichever client can reach the issuer: the API's own channel for
    /// an in-process one, real HTTP for a container.</summary>
    private HttpClient IssuerClient => this.external ?? Client;

    protected IdentityProviderContractTests()
    {
        this.factory = new TenderoApiFactory(Authority);
        Client = this.factory.CreateClient();
        this.external = Authority is null ? null : new HttpClient();
    }

    public void Dispose()
    {
        this.external?.Dispose();
        Client.Dispose();
        this.factory.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Every test starts here. An issuer that is not running is a skip
    /// with a reason, never a failure of the code under test.</summary>
    private void SkipIfUnavailable()
    {
        if (Unavailable is { } reason)
            Assert.Skip(reason);
    }

    [Fact]
    public async Task It_publishes_a_discovery_document()
    {
        SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var response = await IssuerClient.GetAsync(DiscoveryUrl, ct);
        response.EnsureSuccessStatusCode();

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;

        // With no issuer and no jwks_uri, JwtBearer cannot even begin to validate.
        Assert.False(string.IsNullOrWhiteSpace(document["issuer"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(document["jwks_uri"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(document["token_endpoint"]?.GetValue<string>()));
    }

    /// <summary>
    /// The browser flow, and it is the assertion this suite was missing.
    ///
    /// **Everything above passed while the shop used a `password` grant**, which
    /// meant the application collected a username and a password and posted them
    /// to the issuer — the one thing delegating identity is supposed to remove,
    /// and a flow OAuth 2.1 drops. A contract that only proved a token could be
    /// obtained was measuring the wrong half.
    ///
    /// So both issuers now have to advertise Authorization Code with PKCE. A
    /// development issuer that cannot do it is not standing in for Keycloak; it
    /// is standing in for something easier.
    /// </summary>
    [Fact]
    public async Task It_offers_the_authorization_code_flow_with_PKCE()
    {
        SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var document = JsonNode.Parse(
            await (await IssuerClient.GetAsync(DiscoveryUrl, ct)).Content.ReadAsStringAsync(ct))!;

        var authorize = document["authorization_endpoint"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(authorize), "No authorization_endpoint: there is no login page to send anybody to.");

        Assert.Contains("code", Values(document, "response_types_supported"));

        // S256 specifically. `plain` is in RFC 7636 and is a challenge that
        // challenges nothing, so an issuer offering only that would satisfy a
        // looser assertion and none of the protection.
        Assert.Contains("S256", Values(document, "code_challenge_methods_supported"));

        Assert.Contains("authorization_code", Values(document, "grant_types_supported"));
    }

    /// <summary>
    /// The authorization endpoint answers a well-formed request with something a
    /// person can act on, rather than an error.
    ///
    /// What comes back differs — Keycloak renders its login form, the
    /// development issuer renders three buttons — so this asserts only what is
    /// common: it is reached, it is HTML, and it does not refuse. Asserting the
    /// CONTENT would be asserting one implementation, which is the mistake this
    /// suite already made once with the JWKS path.
    /// </summary>
    [Fact]
    public async Task Its_authorization_endpoint_renders_a_page()
    {
        SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var document = JsonNode.Parse(
            await (await IssuerClient.GetAsync(DiscoveryUrl, ct)).Content.ReadAsStringAsync(ct))!;

        var authorize = document["authorization_endpoint"]!.GetValue<string>();

        // A verifier and its S256 challenge, exactly as a browser client builds them.
        const string verifier = "tendero-contract-suite-verifier-0123456789abcdef";
        var challenge = Base64UrlEncoder.Encode(
            SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var query =
            $"?response_type=code&client_id=tendero-app" +
            $"&redirect_uri={Uri.EscapeDataString("http://localhost:4201")}" +
            $"&scope={Uri.EscapeDataString("openid profile")}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=contract";

        var response = await IssuerClient.GetAsync(authorize + query, ct);

        Assert.True(
            response.StatusCode is HttpStatusCode.OK,
            $"The authorization endpoint refused a well-formed request: {(int)response.StatusCode}.");

        Assert.Contains("html", response.Content.Headers.ContentType?.MediaType ?? string.Empty);
    }

    /// <summary>Every value of a discovery array, or an empty list when it is absent.</summary>
    private static string[] Values(JsonNode document, string property) =>
        document[property]?.AsArray().Select(value => value!.GetValue<string>()).ToArray() ?? [];

    [Fact]
    public async Task It_publishes_a_signing_key()
    {
        SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        // It FOLLOWS the jwks_uri the discovery document advertises rather than
        // asking a fixed path, and that is not a generalisation for its own sake:
        // the development issuer serves `/.well-known/jwks.json` and Keycloak
        // serves `/protocol/openid-connect/certs`. A contract that hard-coded
        // either was describing one implementation.
        //
        // It is also what JwtBearer actually does, so the test now exercises the
        // path the runtime takes instead of a parallel one that happened to
        // agree.
        var discovery = JsonNode.Parse(
            await (await IssuerClient.GetAsync(DiscoveryUrl, ct)).Content.ReadAsStringAsync(ct))!;

        var response = await IssuerClient.GetAsync(discovery["jwks_uri"]!.GetValue<string>(), ct);
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
        SkipIfUnavailable();
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
        SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;

        var response = await Client.PostAsync("/api/search/reindex", content: null, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        SkipIfUnavailable();
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
        SkipIfUnavailable();
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
    /// <summary>Null: it lives inside the API process.</summary>
    protected override string? Authority => null;

    protected override string ShopkeeperSubject => "juanlu";

    protected override async Task<string> GetTokenAsync(string subject, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(
            [new("grant_type", "password"), new("username", subject)]);

        var response = await Client.PostAsync("/dev-issuer/connect/token", form, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!;
        return body["access_token"]!.GetValue<string>();
    }
}

/// <summary>
/// **Keycloak, honouring the same contract.** This class is the phase's answer
/// to a question phase 0 could only assert: was the identity provider really a
/// port, or only called one?
///
/// It does NOT start Keycloak, and that is this repository's rule rather than a
/// shortcut. `tools/SearchEval` takes an Elasticsearch URL, the Playwright specs
/// take a running stack, and both say the same thing in their own comments —
/// starting infrastructure from a test builds a second, worse AppHost. So this
/// takes a URL, and skips with a reason when there is none.
///
///   dotnet run --project src/AppHost
///   docker ps --filter name=keycloak          # to read the published port
///   TENDERO_KEYCLOAK_AUTHORITY=https://localhost:PORT/realms/tendero dotnet test
///
/// **What it proved.** The suite it inherits was written when the only issuer
/// lived in the API's own process, and it encoded that three times over: a
/// relative path, the test server's channel, and a JWKS at
/// `/.well-known/jwks.json` — which is the development issuer's location and not
/// Keycloak's. The roadmap promised Keycloak would inherit this "without
/// changing a line". It did not, and the changes are the finding: **a contract
/// suite with one implementation has not been tested as a contract.**
///
/// What did NOT change is the API. The swap is `Authentication:Authority`
/// pointing somewhere else, which is what phase 0 claimed and what this measures.
/// </summary>
public sealed class KeycloakContractTests : IdentityProviderContractTests
{
    private static readonly string? Configured =
        Environment.GetEnvironmentVariable("TENDERO_KEYCLOAK_AUTHORITY");

    protected override string? Authority => Configured;

    protected override string ShopkeeperSubject => "juanlu";

    protected override string? Unavailable => Configured is null
        ? "Set TENDERO_KEYCLOAK_AUTHORITY to a running realm — see the class comment. "
          + "The development issuer's run of this same suite is what covers the contract meanwhile."
        : null;

    /// <summary>
    /// Keycloak's own token endpoint, with the password grant.
    ///
    /// The PASSWORD is the one honest difference between the two issuers and it
    /// is confined to this method: the development issuer signs for a username
    /// alone because it has no credentials to check, and Keycloak will not. That
    /// difference belongs in the adapter — everything above it is unchanged.
    /// </summary>
    protected override async Task<string> GetTokenAsync(
        string subject, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(
        [
            new("grant_type", "password"),
            new("client_id", "tendero-app"),
            new("username", subject),
            // Seeded in keycloak/realms/tendero-realm.json, and the same as the
            // username on purpose: it is a development realm, and a password
            // worth protecting would be a password worth not committing.
            new("password", subject)
        ]);

        // Its own client, because the realm is served over HTTPS with a
        // self-signed development certificate. The exception is scoped to this
        // call rather than to the API's own validation, which still checks every
        // signature it is handed.
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        using var client = new HttpClient(handler);

        var response = await client.PostAsync(
            $"{Authority}/protocol/openid-connect/token", form, cancellationToken);

        response.EnsureSuccessStatusCode();

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!;
        return body["access_token"]!.GetValue<string>();
    }
}
