using System.Security.Cryptography;
using System.Text;

namespace ElGuerre.Tendero.Pricing.Domain;

/// <summary>
/// SHA-256 over a canonical string, in one place.
///
/// It exists because a quote's fingerprint has to cover TWO things that get
/// forgotten separately: what the shopper asked for (lines, quantities, coupons)
/// and the data they were answered with (the tariffs and promotions in force).
/// With only the first, changing a price in the backoffice would leave live
/// quotes promising an amount nobody honours any more — which is exactly the
/// failure checkout revalidation exists to catch.
///
/// Same reasoning as <see cref="ElGuerre.Tendero.SharedKernel.ImageId"/>: an
/// identity derived from content cannot silently point at different content.
/// </summary>
public static class Fingerprints
{
    public static string Of(IEnumerable<string> parts) => Of(string.Join('\n', parts));

    public static string Of(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..32];
}
