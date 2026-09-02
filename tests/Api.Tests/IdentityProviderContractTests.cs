using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// Lo que cualquier emisor de identidad tiene que cumplir para que Tendero
/// funcione con él. Es abstracta a propósito: el emisor de desarrollo la hereda
/// hoy, y Keycloak la heredará cuando llegue, **sin cambiar una línea**.
///
/// Ese es el punto de que la identidad sea un puerto (ADR 0003 aplicado a un
/// sistema externo que no es nuestro). El día del cambio, la pregunta "¿se puede
/// sustituir?" tiene una respuesta ejecutable en vez de una opinión — y un
/// Keycloak que no pase esto es un Keycloak que habría roto producción.
///
/// El contrato no es una interfaz de C#: es OIDC. Discovery, JWKS, y un token
/// que la API valide.
/// </summary>
public abstract class IdentityProviderContractTests : IDisposable
{
    private readonly TenderoApiFactory _factory = new();

    protected HttpClient Client { get; }

    /// <summary>Ruta base del emisor bajo prueba.</summary>
    protected abstract string IssuerPath { get; }

    /// <summary>Una identidad con rol de tendero que el emisor sepa firmar.</summary>
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

        // Sin issuer ni jwks_uri, JwtBearer no puede ni empezar a validar.
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
        // `kid` es lo que permite rotar: sin él, cambiar de clave invalida todo
        // token en vuelo en vez de solaparse.
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

        // No se comprueba 200: reindexar necesita Postgres y Elasticsearch, y
        // aquí no hay ninguno. Lo que se comprueba es que la AUTORIZACIÓN pasó,
        // que es lo único que este contrato mide.
        //
        // El mensaje lleva la cabecera WWW-Authenticate porque es donde JwtBearer
        // dice POR QUÉ rechazó — firma, emisor, audiencia o caducidad — y sin
        // ella un 401 es indistinguible de otro.
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

        // Cambiar un carácter de la firma: el payload sigue siendo válido y la
        // firma ya no. Es el fallo que ValidateIssuerSigningKey existe para
        // atrapar, y el que un emisor falso mal hecho dejaría pasar.
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

        // El catálogo es público por decisión escrita, no por olvido. Si alguien
        // pusiera una política aquí, esto lo diría antes que un usuario.
        var response = await Client.GetAsync("/api/catalog/products", ct);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Cómo se pide un token a ESTE emisor.</summary>
    protected abstract Task<string> GetTokenAsync(string subject, CancellationToken cancellationToken);
}

/// <summary>
/// El emisor de desarrollo, cumpliendo el contrato. Tres líneas, que es lo que
/// debería costar añadir un adaptador cuando la suite está bien escrita —
/// exactamente como <c>SeedCatalogConnectorContractTests</c>.
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
