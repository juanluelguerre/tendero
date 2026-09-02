using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace ElGuerre.Tendero.DevIssuer;

/// <summary>
/// Una identidad que el emisor de desarrollo sabe firmar. Son las mismas que
/// trae el realm de Keycloak cuando llegue: si divergen, cada fixture de test se
/// bifurca en dos.
/// </summary>
public sealed record DevIdentity(string Subject, string Name, string Role, bool IsAgent = false);

public sealed class DevIssuerOptions
{
    public const string SectionName = "DevIssuer";

    public string Audience { get; set; } = "tendero-api";

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Tres roles, que es la profundidad de laboratorio que pide CLAUDE.md.
    /// Un agente es un principal distinto de una persona, no una persona con
    /// otro rol — por eso lleva su propia marca y no sólo <c>Role</c>.
    /// </summary>
    public List<DevIdentity> Identities { get; set; } =
    [
        new("ana", "Ana Ruiz", "shopper"),
        new("juanlu", "Juan Luis", "shopkeeper"),
        new("claude-desktop", "Claude Desktop", "agent", IsAgent: true)
    ];
}

/// <summary>
/// La clave con la que se firma, viva sólo mientras el proceso.
///
/// Generarla al arrancar y no commitearla es deliberado: una clave privada en el
/// repositorio es una clave privada filtrada, por mucho que diga "dev" al lado.
/// El coste es que los tokens no sobreviven a un reinicio, que en desarrollo es
/// exactamente lo que uno espera.
/// </summary>
public sealed class DevSigningKey : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);

    public string KeyId { get; } = Guid.NewGuid().ToString("N");

    public RsaSecurityKey SecurityKey => new(_rsa) { KeyId = KeyId };

    public JsonWebKey PublicJsonWebKey()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);

        return new JsonWebKey
        {
            Kty = "RSA",
            Use = "sig",
            Alg = SecurityAlgorithms.RsaSha256,
            Kid = KeyId,
            N = Base64UrlEncoder.Encode(parameters.Modulus),
            E = Base64UrlEncoder.Encode(parameters.Exponent)
        };
    }

    public void Dispose() => _rsa.Dispose();
}
