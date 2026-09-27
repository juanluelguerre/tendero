using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ElGuerre.Tendero.Ucp.Negotiation;

/// <summary>What a platform's profile says it speaks: capability name to the versions it supports.</summary>
public sealed record PlatformProfile(IReadOnlyDictionary<string, IReadOnlySet<string>> Capabilities);

/// <summary>Why a platform profile could not be used — each one UCP's own error code.</summary>
public enum ProfileProblem
{
    /// <summary>`invalid_profile_url` — absent, not a URL, or a host this shop does not fetch from.</summary>
    InvalidUrl,

    /// <summary>`profile_unreachable` — the fetch failed.</summary>
    Unreachable,

    /// <summary>`profile_malformed` — fetched, and not a UCP profile.</summary>
    Malformed,
}

/// <summary>
/// The platform profiles an agent names in `UCP-Agent`, fetched to negotiate
/// (UCP §Negotiation: the business MUST fetch and validate it). A port, because
/// fetching a URL a caller chose is exactly the server-side request forgery the
/// specification warns about — the adapter decides which hosts are ever fetched.
/// </summary>
public interface IPlatformProfiles
{
    Task<(PlatformProfile? Profile, ProfileProblem? Problem)> GetAsync(string profileUrl, CancellationToken cancellationToken);
}

public sealed class UcpOptions
{
    public const string SectionName = "Ucp";

    /// <summary>Where the shop's pages are: order permalinks and the page a person continues a checkout on.</summary>
    public string StorefrontUrl { get; set; } = "http://localhost:4200";

    /// <summary>
    /// The only hosts a platform profile is fetched from. Empty means none: a shop
    /// does not fetch whatever an agent names. Development trusts <c>localhost</c>,
    /// where this API publishes its seeded agents' profiles.
    /// </summary>
    public string[] TrustedPlatformHosts { get; set; } = [];

    /// <summary>How long a fetched profile is trusted before it is fetched again.</summary>
    public TimeSpan ProfileCacheLifetime { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// The `UCP-Agent` request header: an RFC 8941 dictionary whose <c>profile</c> member
/// is the platform profile URL — <c>profile="https://agent.example/profile"</c>.
/// </summary>
public static partial class UcpAgentHeader
{
    public const string Name = "UCP-Agent";

    public static string? ProfileOf(string? header) =>
        header is null ? null : Profile().Match(header) is { Success: true } match ? match.Groups[1].Value : null;

    [GeneratedRegex("(?:^|[;,\\s])profile\\s*=\\s*\"([^\"]+)\"")]
    private static partial Regex Profile();
}

/// <summary>
/// UCP's intersection algorithm (§Negotiation Protocol): keep what both sides
/// name at a version both speak, then drop every extension whose parent did not
/// survive, until nothing more drops.
/// </summary>
public static class UcpNegotiation
{
    public static IReadOnlySet<string> Intersect(IEnumerable<IUcpCapability> business, PlatformProfile platform)
    {
        var offered = business.ToList();
        var active = offered
            .Where(capability => platform.Capabilities.TryGetValue(capability.Id, out var versions) && versions.Contains(capability.Version))
            .Select(capability => capability.Id)
            .ToHashSet(StringComparer.Ordinal);

        bool pruned;
        do
        {
            pruned = active.RemoveWhere(id => offered.First(capability => capability.Id == id).Extends is { } parent && !active.Contains(parent)) > 0;
        }
        while (pruned);

        return active;
    }
}

/// <summary>
/// Fetches a platform profile over HTTP from a trusted host only, and remembers it
/// for a while. Never follows a redirect to somewhere else: a trusted host that
/// redirects is not a licence to fetch where it points.
/// </summary>
public sealed class HttpPlatformProfiles(HttpClient http, Microsoft.Extensions.Options.IOptions<UcpOptions> options, TimeProvider clock) : IPlatformProfiles
{
    private static readonly ConcurrentDictionary<string, (PlatformProfile Profile, DateTimeOffset Until)> Cache = new(StringComparer.Ordinal);

    public async Task<(PlatformProfile? Profile, ProfileProblem? Problem)> GetAsync(string profileUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(profileUrl, UriKind.Absolute, out var uri)
            || !options.Value.TrustedPlatformHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
            || (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
            return (null, ProfileProblem.InvalidUrl);

        var now = clock.GetUtcNow();
        if (Cache.TryGetValue(uri.AbsoluteUri, out var cached) && cached.Until > now)
            return (cached.Profile, null);

        JsonDocument document;
        try
        {
            using var response = await http.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (null, ProfileProblem.Unreachable);
            document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken) ?? throw new JsonException("empty");
        }
        catch (HttpRequestException)
        {
            return (null, ProfileProblem.Unreachable);
        }
        catch (JsonException)
        {
            return (null, ProfileProblem.Malformed);
        }

        using (document)
        {
            if (Parse(document.RootElement) is not { } profile)
                return (null, ProfileProblem.Malformed);

            Cache[uri.AbsoluteUri] = (profile, now + options.Value.ProfileCacheLifetime);
            return (profile, null);
        }
    }

    /// <summary>`ucp.capabilities`: a map of name to a list of entries, each with a `version`.</summary>
    internal static PlatformProfile? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("ucp", out var ucp) || ucp.ValueKind != JsonValueKind.Object
            || !ucp.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object)
            return null;

        var parsed = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        foreach (var capability in capabilities.EnumerateObject())
        {
            if (capability.Value.ValueKind != JsonValueKind.Array)
                return null;

            parsed[capability.Name] = capability.Value.EnumerateArray()
                .Where(entry => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
                .Select(entry => entry.GetProperty("version").GetString()!)
                .ToHashSet(StringComparer.Ordinal);
        }

        return new PlatformProfile(parsed);
    }
}
