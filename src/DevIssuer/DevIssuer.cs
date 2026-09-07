using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace ElGuerre.Tendero.DevIssuer;

/// <summary>
/// An identity the development issuer knows how to sign for. They are the same
/// ones Keycloak's realm will bring when it arrives: if they diverge, every test
/// fixture forks in two.
/// </summary>
public sealed record DevIdentity(string Subject, string Name, string Role, bool IsAgent = false);

public sealed class DevIssuerOptions
{
    public const string SectionName = "DevIssuer";

    public string Audience { get; set; } = "tendero-api";

    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Three roles, which is the laboratory depth CLAUDE.md asks for. An agent is
    /// a different principal from a person, not a person with another role — which
    /// is why it carries its own marker and not just a <c>Role</c>.
    /// </summary>
    public List<DevIdentity> Identities { get; set; } =
    [
        new("ana", "Ana Ruiz", "shopper"),
        new("juanlu", "Juan Luis", "shopkeeper"),
        new("claude-desktop", "Claude Desktop", "agent", IsAgent: true)
    ];
}

/// <summary>
/// The key it signs with, alive only for as long as the process.
///
/// Generating it at startup and not committing it is deliberate: a private key
/// in the repository is a leaked private key, however loudly it says "dev" next
/// to it. The cost is that tokens do not survive a restart, which in development
/// is exactly what anybody expects.
/// </summary>
public sealed class DevSigningKey : IDisposable
{
    private readonly RSA rsa = RSA.Create(2048);

    public string KeyId { get; } = Guid.NewGuid().ToString("N");

    public RsaSecurityKey SecurityKey => new(this.rsa) { KeyId = KeyId };

    public JsonWebKey PublicJsonWebKey()
    {
        var parameters = this.rsa.ExportParameters(includePrivateParameters: false);

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

    public void Dispose() => this.rsa.Dispose();
}
