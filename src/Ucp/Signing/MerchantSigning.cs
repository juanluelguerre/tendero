using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElGuerre.Tendero.Ucp.Signing;

/// <summary>
/// The shop's signing key, as a port: what signs <c>ap2.merchant_authorization</c>
/// (P11-4), and what will sign order webhooks. A port because where a private key
/// lives — a file, a vault, an HSM that never lets it out — is an external system,
/// and a handler should not know which.
/// </summary>
public interface IMerchantSigner : IJwsSigner
{
    /// <summary>
    /// The public keys published under the profile's <c>keys</c>: the one signing
    /// now, and any still verifying what an older one signed. A checkout mandate
    /// comes back carrying our signature, so verification looks here by kid.
    /// </summary>
    IReadOnlyList<Jwk> PublishedKeys { get; }
}

public sealed class MerchantSigningOptions
{
    public const string SectionName = "Ucp:Signing";

    /// <summary>
    /// A PKCS#8 or SEC1 PEM ECDSA private key (P-256, P-384 or P-521). Empty in
    /// development, where a key is made at start-up.
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    /// <summary>The kid to publish it under; its RFC 7638 thumbprint when empty.</summary>
    public string? KeyId { get; set; }
}

/// <summary>
/// A key held in this process: the configured one, or — with none configured — a
/// P-256 key made at start-up.
///
/// **An ephemeral key is honest in development and wrong anywhere else**, and it is
/// said at start-up rather than discovered later: a restart publishes a new key, so
/// a checkout authorised before it no longer verifies, and two instances would each
/// publish a key the other does not hold. A shop in production configures one.
/// </summary>
public sealed class LocalMerchantSigner : IMerchantSigner, IDisposable
{
    private readonly ECDsa key;
    private readonly EcdsaJwsSigner signer;

    public LocalMerchantSigner(IOptions<MerchantSigningOptions> options, ILogger<LocalMerchantSigner> logger)
    {
        key = ECDsa.Create();
        if (string.IsNullOrWhiteSpace(options.Value.PrivateKeyPem))
        {
            key.GenerateKey(ECCurve.NamedCurves.nistP256);
            logger.LogWarning(
                "No {Section}:PrivateKeyPem configured: signing with a key made at start-up. Checkouts authorised before a restart will not verify.",
                MerchantSigningOptions.SectionName);
        }
        else
        {
            key.ImportFromPem(options.Value.PrivateKeyPem);
        }

        signer = new EcdsaJwsSigner(key, string.IsNullOrWhiteSpace(options.Value.KeyId) ? null : options.Value.KeyId);
        PublishedKeys = [signer.PublicKey];
    }

    public string KeyId => signer.KeyId;

    public string Algorithm => signer.Algorithm;

    public IReadOnlyList<Jwk> PublishedKeys { get; }

    public byte[] Sign(ReadOnlySpan<byte> signingInput) => signer.Sign(signingInput);

    public void Dispose() => key.Dispose();
}
