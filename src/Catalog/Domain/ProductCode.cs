using System.Security.Cryptography;

namespace ElGuerre.Tendero.Catalog.Domain;

/// <summary>
/// The short, public, permanent name of a product — Tendero's ASIN.
///
/// It exists because the URL needs a key and neither of the two obvious
/// candidates can be one (ADR 0026).
///
/// <see cref="ProductId"/> cannot: it is a GUID v7, so it is 36 characters of
/// noise in a URL and, worse, it carries the creation timestamp in its leading
/// bits. Publishing it would let anybody sort the catalogue by the date each
/// product was added and read off the rate at which the shop grows.
///
/// The SLUG cannot: it is derived from a name, so two products called the same
/// thing produce the same slug and nothing in a database can stop them —
/// uniqueness across the values of a jsonb object is not expressible as a
/// constraint. And it CHANGES when the product is renamed, which turns every
/// inbound link into a 404 unless the catalogue also keeps a history of every
/// address it has ever handed out.
///
/// A code answers both at once by being the thing neither of them is: short,
/// meaningless, and never regenerated. The slug stays in the URL for humans and
/// for search engines, but it is decoration — change it freely, the link still
/// resolves. That is the shape Amazon, eBay and Zalando all converged on, and
/// the reason is this one.
/// </summary>
public static class ProductCode
{
    /// <summary>
    /// Crockford's base32: the digits, then the letters with <c>I</c>,
    /// <c>L</c>, <c>O</c> and <c>U</c> removed.
    ///
    /// The first three go because they are the characters people get wrong when
    /// a code is read down a phone, copied off a screen or typed from a printed
    /// receipt — <c>I</c> and <c>L</c> against <c>1</c>, <c>O</c> against
    /// <c>0</c>. <c>U</c> goes for Crockford's own reason: with the vowels
    /// thinned out, a random string is far less likely to spell something the
    /// shop would rather not print on an invoice.
    /// </summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>
    /// Ten characters, which is fifty bits and also exactly what an ASIN is.
    /// Long enough that a catalogue of millions never collides in practice,
    /// short enough to fit in a URL without looking like an error.
    ///
    /// Practice is not the guarantee, though: the unique index on the column is.
    /// </summary>
    public const int Length = 10;

    /// <summary>
    /// A fresh code. Cryptographic randomness rather than <c>Random</c> because
    /// this is a public identifier, and a predictable one lets a stranger
    /// enumerate the catalogue — including what has not been published yet.
    /// </summary>
    public static string New()
    {
        var characters = new char[Length];
        var bytes = RandomNumberGenerator.GetBytes(Length);

        for (var i = 0; i < Length; i++)
        {
            // The alphabet is exactly 32 characters, so masking the low five
            // bits is uniform. Taking a byte modulo a non-power-of-two would
            // not be, and a biased identifier is a smaller keyspace than the
            // one you think you have.
            characters[i] = Alphabet[bytes[i] & 0b1_1111];
        }

        return new string(characters);
    }

    /// <summary>
    /// The canonical form of whatever arrived in the URL.
    ///
    /// Case is folded up because a code is uppercase everywhere it is shown, and
    /// people lowercase URLs — by hand, in a CMS, in an email client that
    /// "tidies" links. Answering 404 to a correct code in the wrong case is a
    /// lost visitor for no reason at all.
    ///
    /// Crockford's own substitutions are applied for the same reason the
    /// ambiguous letters were removed: somebody who reads <c>0</c> as <c>O</c>
    /// should still land on the product. It never generates them; it only
    /// forgives them.
    /// </summary>
    public static string Normalise(string code) =>
        code.Trim()
            .ToUpperInvariant()
            .Replace('I', '1')
            .Replace('L', '1')
            .Replace('O', '0');

    /// <summary>Whether a string could be a code at all — checked before it
    /// reaches the database, so a malformed URL is a 404 and not a query.</summary>
    public static bool IsWellFormed(string code) =>
        code.Length == Length && code.All(Alphabet.Contains);
}
