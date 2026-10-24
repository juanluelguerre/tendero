using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Accounts.Adapters;
using ElGuerre.Tendero.Accounts.Ports;
using ElGuerre.Tendero.Accounts.Tests.Identity;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>Keycloak's adapter of the provisioning port passes the contract every adapter does.</summary>
public sealed class KeycloakIdentityProvisionerContractTests : IdentityProvisionerContractTests
{
    protected override IIdentityProvisioner CreateProvisioner() => KeycloakIdentityProvisionerTests.AProvisioner().Provisioner;
}

/// <summary>
/// Keycloak's admin API as the provisioner uses it (phase 16, S2b, P16-17; ADR
/// 0067): a token for its service account by the client-credentials grant, a
/// search by exact email, and a user made enabled, with the person's name and
/// language and **no credential** — they set one with *forgot password*.
/// </summary>
public sealed class KeycloakIdentityProvisionerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static (KeycloakIdentityProvisioner Provisioner, FakeKeycloak Keycloak) AProvisioner()
    {
        var keycloak = new FakeKeycloak();
        var options = Options.Create(new KeycloakProvisioningOptions
        {
            BaseUrl = new Uri("https://localhost:38443/"),
            Realm = "tendero",
            ClientId = "tendero-provisioning",
            ClientSecret = "secret",
        });
        return (new KeycloakIdentityProvisioner(new HttpClient(keycloak), options), keycloak);
    }

    [Fact]
    public async Task A_person_is_made_enabled_with_their_name_and_language_and_no_credential()
    {
        var (provisioner, keycloak) = AProvisioner();

        var subject = await provisioner.ProvisionAsync(new IdentityToProvision("Ana@Example.com", "Ana", "Ruiz", "es"), Ct);

        var user = keycloak.Users[subject];
        Assert.Equal(("ana@example.com", "ana@example.com", "Ana", "Ruiz"), ((string)user["username"]!, (string)user["email"]!, (string)user["firstName"]!, (string)user["lastName"]!));
        Assert.True((bool)user["enabled"]!);
        Assert.Equal("es", (string)user["attributes"]!["locale"]![0]!);
        Assert.Null(user["credentials"]);
    }

    /// <summary>The service account's token, by the client-credentials grant, on every call to the admin API.</summary>
    [Fact]
    public async Task The_admin_api_is_called_with_the_service_accounts_token()
    {
        var (provisioner, keycloak) = AProvisioner();

        await provisioner.ProvisionAsync(new IdentityToProvision("ana@example.com", null, null, null), Ct);

        Assert.Equal("grant_type=client_credentials&client_id=tendero-provisioning&client_secret=secret", Assert.Single(keycloak.TokenRequests));
        Assert.All(keycloak.AdminCalls, authorization => Assert.Equal("Bearer admin-token", authorization));
    }

    /// <summary>
    /// Somebody else made the same identity between the search and the creation:
    /// Keycloak answers 409, and the provisioner answers with the identity that won.
    /// </summary>
    [Fact]
    public async Task An_identity_made_meanwhile_is_the_answer_not_an_error()
    {
        var (provisioner, keycloak) = AProvisioner();
        keycloak.MakeBeforeCreate = "ana@example.com";

        var subject = await provisioner.ProvisionAsync(new IdentityToProvision("ana@example.com", "Ana", null, null), Ct);

        Assert.Equal(keycloak.Users.Single().Key, subject);
    }

    [Fact]
    public async Task A_refusal_says_what_keycloak_answered()
    {
        var (provisioner, keycloak) = AProvisioner();
        keycloak.RefuseCreate = HttpStatusCode.Forbidden;

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provisioner.ProvisionAsync(new IdentityToProvision("ana@example.com", null, null, null), Ct));

        Assert.Contains("403", refused.Message, StringComparison.Ordinal);
    }
}

/// <summary>Keycloak's token endpoint and the slice of its admin API the provisioner uses, in memory.</summary>
internal sealed class FakeKeycloak : HttpMessageHandler
{
    public Dictionary<string, JsonObject> Users { get; } = [];

    public List<string> TokenRequests { get; } = [];

    public List<string?> AdminCalls { get; } = [];

    /// <summary>An email somebody else makes between the provisioner's search and its creation.</summary>
    public string? MakeBeforeCreate { get; set; }

    public HttpStatusCode? RefuseCreate { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path == "/realms/tendero/protocol/openid-connect/token")
        {
            TokenRequests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return Json(new JsonObject { ["access_token"] = "admin-token", ["expires_in"] = 300 });
        }

        if (path != "/admin/realms/tendero/users")
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        AdminCalls.Add(request.Headers.Authorization?.ToString());

        if (request.Method == HttpMethod.Get)
        {
            var email = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["email"];
            var found = Users.Where(user => (string)user.Value["email"]! == email)
                .Select(user => new JsonObject { ["id"] = user.Key, ["email"] = (string)user.Value["email"]! });
            return Json(new JsonArray([.. found]));
        }

        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        if (RefuseCreate is { } refusal)
            return new HttpResponseMessage(refusal);

        if (MakeBeforeCreate is { } raced)
        {
            Users[Guid.NewGuid().ToString()] = new JsonObject { ["email"] = raced, ["username"] = raced };
            MakeBeforeCreate = null;
        }

        if (Users.Values.Any(user => (string)user["email"]! == (string)body["email"]!))
            return new HttpResponseMessage(HttpStatusCode.Conflict);

        var id = Guid.NewGuid().ToString();
        Users[id] = body;
        var created = new HttpResponseMessage(HttpStatusCode.Created);
        created.Headers.Location = new Uri($"https://localhost:38443/admin/realms/tendero/users/{id}");
        return created;
    }

    private static HttpResponseMessage Json(JsonNode node) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(node, options: new JsonSerializerOptions()) };
}
