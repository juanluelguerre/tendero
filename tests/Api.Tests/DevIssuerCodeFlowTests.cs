using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The whole redirect, driven without a browser.
///
/// The contract suite asserts that both issuers ADVERTISE Authorization Code
/// with PKCE and that the authorization endpoint renders something. It cannot
/// assert more than that, because what happens next differs: Keycloak wants a
/// username and a password typed into its form, and the development issuer wants
/// a button pressed. So the round trip is tested here, against the issuer whose
/// login page has no secrets in it.
///
/// **What this proves that nothing else does** is that PKCE is enforced rather
/// than accepted. A verifier that does not match the challenge, a code spent
/// twice, and a redirect URI that changed between the two legs are the three
/// ways an authorization code goes wrong, and all three are refused here.
/// </summary>
public sealed class DevIssuerCodeFlowTests : IDisposable
{
    private const string RedirectUri = "http://localhost:4201";
    private const string ClientId = "tendero-app";

    /// <summary>A verifier the way a browser makes one: 43 to 128 unreserved characters.</summary>
    private const string Verifier = "tendero-code-flow-verifier-0123456789-abcdefghij";

    private static string Challenge(string verifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private readonly TenderoApiFactory _factory = new();
    private readonly HttpClient _client;

    public DevIssuerCodeFlowTests() =>
        // Redirects are NOT followed: the redirect is the thing under test, and a
        // client that chases it turns the assertion into "did localhost:4201
        // answer", which nothing here is serving.
        _client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Picking_an_identity_and_spending_the_code_returns_a_token()
    {
        var ct = TestContext.Current.CancellationToken;

        var code = await AuthorizeAsync("juanlu", Challenge(Verifier), ct);

        var token = await ExchangeAsync(code, Verifier, RedirectUri, ct);

        Assert.NotNull(token);

        // **The RFC 6749 names, and this is the assertion that was missing.**
        // The first version of this test read `accessToken` and `idToken`,
        // because that is what the record serialised to — so it agreed with the
        // implementation and with no OIDC client anywhere. Nothing failed: the
        // library found no token, the person stayed signed out, and the guard
        // sent them back to the door on every attempt.
        //
        // A test written against its own adapter's field names is not testing a
        // wire format, it is describing one.
        Assert.False(string.IsNullOrWhiteSpace(token!["access_token"]?.GetValue<string>()));
        Assert.Equal("Bearer", token["token_type"]?.GetValue<string>());
        Assert.True(token["expires_in"]?.GetValue<int>() > 0);

        // The id_token is the half the CLIENT validates, and it is what the
        // password grant never produced. Its audience is the client and not the
        // API, which is the distinction that makes it a different token rather
        // than a copy.
        Assert.NotNull(token["id_token"]?.GetValue<string>());
    }

    [Fact]
    public async Task A_verifier_that_does_not_match_the_challenge_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;

        var code = await AuthorizeAsync("juanlu", Challenge(Verifier), ct);

        // Somebody who intercepted the code but never saw the verifier. This is
        // the entire attack PKCE exists for, and it is the reason the challenge
        // is sent up front rather than with the exchange.
        var token = await ExchangeAsync(code, "a-different-verifier-that-is-long-enough-01234", RedirectUri, ct);

        Assert.Null(token);
    }

    [Fact]
    public async Task A_code_can_only_be_spent_once()
    {
        var ct = TestContext.Current.CancellationToken;

        var code = await AuthorizeAsync("juanlu", Challenge(Verifier), ct);

        Assert.NotNull(await ExchangeAsync(code, Verifier, RedirectUri, ct));

        // Single use is enforced by REMOVING the code on the first read rather
        // than by a flag somebody has to remember to set, which is why a replay
        // finds nothing at all.
        Assert.Null(await ExchangeAsync(code, Verifier, RedirectUri, ct));
    }

    [Fact]
    public async Task A_code_issued_for_one_redirect_uri_cannot_be_spent_against_another()
    {
        var ct = TestContext.Current.CancellationToken;

        var code = await AuthorizeAsync("juanlu", Challenge(Verifier), ct);

        var token = await ExchangeAsync(code, Verifier, "http://localhost:4200", ct);

        Assert.Null(token);
    }

    [Fact]
    public async Task An_authorization_request_without_a_challenge_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync(
            "/dev-issuer/connect/authorize" +
            $"?response_type=code&client_id={ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&state=x", ct);

        // PKCE is required and not merely supported. A fake that accepts a
        // weaker request than the real one teaches the client a habit that
        // breaks on the swap.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The open-redirect guard, which is the only thing standing between a
    /// development issuer and a way to bounce a browser anywhere.
    /// </summary>
    [Fact]
    public async Task It_refuses_to_send_the_browser_off_the_machine()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync(
            "/dev-issuer/connect/authorize" +
            $"?response_type=code&client_id={ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString("https://example.com/steal")}" +
            $"&code_challenge={Challenge(Verifier)}&code_challenge_method=S256&state=x", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Walks the two legs a browser walks: render the page, press the identity,
    /// and read the code out of the Location header.
    /// </summary>
    private async Task<string> AuthorizeAsync(string subject, string challenge, CancellationToken ct)
    {
        var query =
            $"?response_type=code&client_id={ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&scope={Uri.EscapeDataString("openid profile")}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=contract";

        var page = await _client.GetAsync($"/dev-issuer/connect/authorize{query}", ct);
        page.EnsureSuccessStatusCode();

        // The page really does offer this identity. Following a link the picker
        // does not render would be testing the endpoint and not the flow.
        Assert.Contains(subject, await page.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);

        var pick = await _client.GetAsync(
            $"/dev-issuer/connect/authorize/pick{query}&subject={subject}", ct);

        Assert.Equal(HttpStatusCode.Redirect, pick.StatusCode);

        var location = pick.Headers.Location!.ToString();
        Assert.StartsWith(RedirectUri, location, StringComparison.Ordinal);

        var parameters = HttpUtility.ParseQueryString(location[location.IndexOf('?')..]);

        // The state comes back untouched, which is what a client compares to
        // know the answer belongs to the request it started.
        Assert.Equal("contract", parameters["state"]);

        return parameters["code"]!;
    }

    /// <summary>The token, or null when the issuer refused. Which failure it was
    /// is deliberately not distinguishable — that is the endpoint's decision.</summary>
    private async Task<JsonNode?> ExchangeAsync(
        string code, string verifier, string redirectUri, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(
        [
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", redirectUri),
            new("code_verifier", verifier),
            new("client_id", ClientId)
        ]);

        var response = await _client.PostAsync("/dev-issuer/connect/token", form, ct);

        return response.IsSuccessStatusCode
            ? JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))
            : null;
    }
}
