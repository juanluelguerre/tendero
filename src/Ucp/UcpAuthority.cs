using System.Net;

namespace ElGuerre.Tendero.Ucp;

/// <summary>
/// UCP's **authority binding**: a reverse-domain name is controlled by whoever
/// owns the domain, and the proof is where the entity's `schema` is served. A
/// platform *MUST* reject a capability whose `schema` URL is not on a host the
/// name reverses to — treated as not present, never activated — so a shop that
/// declares its own extension anywhere else is declaring nothing.
///
/// The specification's derivation, step for step: the URL parses, is `https`
/// and carries no credentials; its host is a domain name of at least two
/// labels; that host, lower-cased and reversed label by label, either **is**
/// the name or is followed in it by a dot. Provenance, not trust: it says who
/// is making the claim and nothing about whether to believe it.
/// </summary>
public static class UcpAuthority
{
    public static bool Binds(string name, string schemaUrl)
    {
        if (!Uri.TryCreate(schemaUrl, UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps
            || url.UserInfo.Length > 0
            || url.HostNameType != UriHostNameType.Dns
            || IPAddress.TryParse(url.Host, out _))
            return false;

        // `IdnHost` is the A-label form the specification compares in.
        var labels = url.IdnHost.TrimEnd('.').ToLowerInvariant().Split('.');
        if (labels.Length < 2 || labels.Any(label => label.Length == 0))
            return false;

        var prefix = string.Join('.', labels.Reverse());

        // On a label boundary: `com.example` is a textual prefix of
        // `com.examplecorp.pay` and not its authority.
        return name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the shop's own extensions bind from schemas published under
    /// <paramref name="ownSchemasUrl"/>: asked of a setting when the shop
    /// starts, so that a wrong one fails there and not in a platform's
    /// negotiation, silently.
    /// </summary>
    public static bool BindsOwn(string ownSchemasUrl) =>
        UcpCapabilities.Declared
            .Where(capability => capability.Schema.StartsWith('/'))
            .All(capability => Binds(capability.Id, UcpCapabilities.Absolute(capability.Schema, ownSchemasUrl)));
}
