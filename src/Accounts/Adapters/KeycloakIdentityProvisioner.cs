using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ElGuerre.Tendero.Accounts.Ports;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Accounts.Adapters;

/// <summary>
/// Where Keycloak's admin API is and who the shop is to it (phase 16, S2b; ADR
/// 0067): a service account that may manage users and nothing else. All four come
/// from configuration; the secret from user secrets, never the repository.
/// </summary>
public sealed class KeycloakProvisioningOptions
{
    public const string Section = "Keycloak:Provisioning";

    /// <summary>Keycloak's base address, the one its realms hang from.</summary>
    public Uri? BaseUrl { get; set; }

    public string Realm { get; set; } = "tendero";

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool IsConfigured =>
        BaseUrl is not null && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// The provisioning port's Keycloak adapter (ADR 0067). It finds an identity by
/// exact email, or makes one — enabled, with the person's name and language, and
/// **no credential**: they set a password with the realm's own *forgot password*,
/// which also proves the address is theirs. It never authenticates anybody.
///
/// A 409 on creation means somebody made the same identity between the search
/// and the creation; the answer is then the identity that won, found again.
/// </summary>
internal sealed class KeycloakIdentityProvisioner(HttpClient http, IOptions<KeycloakProvisioningOptions> options)
    : IIdentityProvisioner
{
    public async Task<string> ProvisionAsync(IdentityToProvision person, CancellationToken cancellationToken = default)
    {
        var email = person.Email.Trim().ToLowerInvariant();
        var token = await TokenAsync(cancellationToken);

        if (await FindAsync(email, token, cancellationToken) is { } existing)
            return existing;

        using var create = Admin(HttpMethod.Post, Users(), token);
        create.Content = JsonContent.Create(new
        {
            username = email,
            email,
            firstName = person.FirstName,
            lastName = person.LastName,
            enabled = true,
            attributes = person.Culture is { } culture ? new Dictionary<string, string[]> { ["locale"] = [culture] } : null,
        });

        using var created = await http.SendAsync(create, cancellationToken);
        if (created.StatusCode == HttpStatusCode.Conflict)
            return await FindAsync(email, token, cancellationToken)
                ?? throw new InvalidOperationException($"Keycloak said {email} exists and then could not find it.");

        if (created.StatusCode != HttpStatusCode.Created || created.Headers.Location is not { } location)
            throw new InvalidOperationException($"Keycloak refused to make an identity for {email} ({(int)created.StatusCode}).");

        return location.Segments[^1].TrimEnd('/');
    }

    private async Task<string?> FindAsync(string email, string token, CancellationToken cancellationToken)
    {
        using var search = Admin(HttpMethod.Get, new Uri($"{Users()}?email={Uri.EscapeDataString(email)}&exact=true"), token);
        using var answer = await http.SendAsync(search, cancellationToken);
        answer.EnsureSuccessStatusCode();

        var users = await answer.Content.ReadFromJsonAsync<KeycloakUser[]>(cancellationToken) ?? [];
        return users.FirstOrDefault(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>The service account's token, by the client-credentials grant.</summary>
    private async Task<string> TokenAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var answer = await http.PostAsync(
            new Uri(settings.BaseUrl!, $"realms/{settings.Realm}/protocol/openid-connect/token"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = settings.ClientId!,
                ["client_secret"] = settings.ClientSecret!,
            }),
            cancellationToken);

        if (!answer.IsSuccessStatusCode)
            throw new InvalidOperationException($"Keycloak refused the provisioning account's credentials ({(int)answer.StatusCode}).");

        return (await answer.Content.ReadFromJsonAsync<Granted>(cancellationToken))?.AccessToken
            ?? throw new InvalidOperationException("Keycloak answered no token.");
    }

    private Uri Users() => new(options.Value.BaseUrl!, $"admin/realms/{options.Value.Realm}/users");

    private static HttpRequestMessage Admin(HttpMethod method, Uri uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private sealed record Granted([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record KeycloakUser(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("email")] string? Email);
}
