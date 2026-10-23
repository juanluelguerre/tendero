using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Shopify;

/// <summary>
/// The store's Admin API token, by the client-credentials grant: the app's id
/// and secret exchanged at the store's OAuth endpoint for a token that lasts
/// 24 hours (https://shopify.dev/docs/apps/build/dev-dashboard/get-api-access-tokens).
/// It is kept and asked for again a few minutes before it would expire, so a
/// long import does not meet a refusal halfway; a refusal still makes it ask
/// once more (<see cref="Forget"/>).
/// </summary>
internal sealed class ShopifyAccessTokens(HttpClient http, IOptions<ShopifyOptions> options, TimeProvider clock)
{
    /// <summary>How long before expiry a token stops being used.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim asking = new(1, 1);
    private (string Token, DateTimeOffset Until)? current;

    public async ValueTask<string> CurrentAsync(CancellationToken ct)
    {
        if (Valid() is { } token)
            return token;

        await this.asking.WaitAsync(ct);
        try
        {
            // Somebody else may have asked while this call waited.
            if (Valid() is { } fresh)
                return fresh;

            var settings = options.Value;
            using var response = await http.PostAsync(
                new Uri($"https://{settings.Store}/admin/oauth/access_token"),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = settings.ClientId!,
                    ["client_secret"] = settings.ClientSecret!,
                }),
                ct);

            if (!response.IsSuccessStatusCode)
                throw new ShopifyException(ShopifyFailure.Unauthorized, $"The store refused the app's credentials ({(int)response.StatusCode}).");

            var granted = await response.Content.ReadFromJsonAsync<Granted>(ct)
                ?? throw new ShopifyException(ShopifyFailure.Unauthorized, "The store answered no token.");

            this.current = (granted.AccessToken, clock.GetUtcNow().AddSeconds(granted.ExpiresIn) - Margin);
            return granted.AccessToken;
        }
        finally
        {
            this.asking.Release();
        }
    }

    /// <summary>Drops the token the store has just refused.</summary>
    public void Forget() => this.current = null;

    private string? Valid() =>
        this.current is { } held && clock.GetUtcNow() < held.Until ? held.Token : null;

    private sealed record Granted(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
