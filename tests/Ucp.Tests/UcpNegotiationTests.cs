using System.Net;
using System.Text.Json;
using ElGuerre.Tendero.Tests;
using ElGuerre.Tendero.Ucp.Capabilities;
using ElGuerre.Tendero.Ucp.Checkout;
using ElGuerre.Tendero.Ucp.Negotiation;
using Microsoft.Extensions.Options;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// UCP's negotiation (P11-1): the platform's profile, fetched only from a host the
/// shop trusts, intersected with what the shop declares — and an extension never
/// outliving the capability it extends.
/// </summary>
public sealed class UcpNegotiationTests
{
    private static PlatformProfile Platform(params (string Name, string Version)[] capabilities) =>
        new(capabilities.ToDictionary(c => c.Name, IReadOnlySet<string> (c) => new HashSet<string> { c.Version }));

    [Fact]
    public void What_both_sides_speak_at_the_same_version_is_active()
    {
        var active = UcpNegotiation.Intersect(UcpCapabilities.Declared, Platform(
            (CheckoutCapability.Name, UcpSpecification.Version),
            (FulfillmentCapability.Name, UcpSpecification.Version),
            (DiscountCapability.Name, "2026-01-11")));

        Assert.Equal([CheckoutCapability.Name, FulfillmentCapability.Name], active.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void An_extension_whose_parent_did_not_survive_is_dropped_too()
    {
        var active = UcpNegotiation.Intersect(UcpCapabilities.Declared, Platform(
            (FulfillmentCapability.Name, UcpSpecification.Version),
            (DiscountCapability.Name, UcpSpecification.Version),
            ("dev.ucp.shopping.catalog.search", UcpSpecification.Version)));

        Assert.Equal(["dev.ucp.shopping.catalog.search"], active);
    }

    [Theory]
    [InlineData("profile=\"https://agent.example/ucp.json\"", "https://agent.example/ucp.json")]
    [InlineData("version=\"2026-08-25\", profile=\"https://agent.example/p\"", "https://agent.example/p")]
    [InlineData("profile = \"https://agent.example/p\"", "https://agent.example/p")]
    [InlineData("myprofile=\"https://agent.example/p\"", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_profile_is_read_from_the_ucp_agent_header(string? header, string? profile) =>
        Assert.Equal(profile, UcpAgentHeader.ProfileOf(header));

    [Fact]
    public async Task No_profile_no_checkout_and_no_checkout_in_common_is_an_error_response()
    {
        var (_, missing) = await UcpCheckoutOperations.NegotiateAsync(null, new Fixed(null, null), TestContext.Current.CancellationToken);
        var (_, unreachable) = await UcpCheckoutOperations.NegotiateAsync("https://agent.example/p", new Fixed(null, ProfileProblem.Unreachable), TestContext.Current.CancellationToken);
        var (_, incompatible) = await UcpCheckoutOperations.NegotiateAsync("https://agent.example/p",
            new Fixed(Platform(("dev.ucp.shopping.catalog.search", UcpSpecification.Version)), null), TestContext.Current.CancellationToken);

        Assert.Equal((400, "invalid_profile_url"), (missing!.Status, ((UcpProtocolError)missing.Body).Code));
        Assert.Equal((424, "profile_unreachable"), (unreachable!.Status, ((UcpProtocolError)unreachable.Body).Code));
        Assert.Equal((200, "capabilities_incompatible"), (incompatible!.Status, ((UcpErrorResponse)incompatible.Body).Messages.Single().Code));
    }

    [Fact]
    public async Task A_checkout_response_carries_only_the_checkout_family()
    {
        var (active, _) = await UcpCheckoutOperations.NegotiateAsync("https://agent.example/p",
            new Fixed(Platform([.. UcpCapabilities.Declared.Select(c => (c.Id, c.Version))]), null), TestContext.Current.CancellationToken);

        Assert.Equal([Ap2MandateCapability.Name, CheckoutCapability.Name, DiscountCapability.Name, FulfillmentCapability.Name],
            active!.Active.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The server-side request forgery the specification warns about: a shop does
    /// not fetch whatever an agent names. An untrusted host, a non-HTTPS remote host
    /// or no URL at all is refused without a request being made.
    /// </summary>
    [Theory]
    [InlineData("https://evil.example/profile.json")]
    [InlineData("http://agent.example/profile.json")]
    [InlineData("not a url")]
    public async Task Only_a_trusted_host_is_ever_fetched(string url)
    {
        var handler = new Recording(HttpStatusCode.OK, "{}");
        var profiles = new HttpPlatformProfiles(new HttpClient(handler), Options.Create(new UcpOptions { TrustedPlatformHosts = ["agent.example"] }), new TestClock());

        var (profile, problem) = await profiles.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Null(profile);
        Assert.Equal(ProfileProblem.InvalidUrl, problem);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_trusted_profile_is_fetched_once_parsed_and_remembered()
    {
        var handler = new Recording(HttpStatusCode.OK, """{"ucp":{"version":"2026-08-25","capabilities":{"dev.ucp.shopping.checkout":[{"version":"2026-08-25"}]}}}""");
        var profiles = new HttpPlatformProfiles(new HttpClient(handler), Options.Create(new UcpOptions { TrustedPlatformHosts = ["agent.example"] }), new TestClock());
        var url = $"https://agent.example/{Guid.NewGuid()}.json";

        var (first, _) = await profiles.GetAsync(url, TestContext.Current.CancellationToken);
        var (second, _) = await profiles.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(["2026-08-25"], first!.Capabilities[CheckoutCapability.Name]);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// A platform's keys are read from its profile, and one this shop cannot name is
    /// skipped rather than the profile refused (P11-4) — every AP2 test built its
    /// profile by hand, so nothing had read <c>keys</c> until phase 11's Stryker close.
    /// </summary>
    [Fact]
    public async Task A_profile_s_keys_are_read_and_a_key_without_a_kid_is_skipped()
    {
        var body = """{"ucp":{"capabilities":{}},"keys":[{"kid":"k1","kty":"EC","crv":"P-256","x":"AA","y":"AA","alg":"ES256"},{"kty":"EC"},"not a key"]}""";
        var profiles = new HttpPlatformProfiles(new HttpClient(new Recording(HttpStatusCode.OK, body)), Options.Create(new UcpOptions { TrustedPlatformHosts = ["agent.example"] }), new TestClock());
        var none = new HttpPlatformProfiles(new HttpClient(new Recording(HttpStatusCode.OK, """{"ucp":{"capabilities":{}},"keys":{"k1":{}}}""")), Options.Create(new UcpOptions { TrustedPlatformHosts = ["agent.example"] }), new TestClock());

        var (profile, _) = await profiles.GetAsync($"https://agent.example/{Guid.NewGuid()}.json", TestContext.Current.CancellationToken);
        var (withoutKeys, _) = await none.GetAsync($"https://agent.example/{Guid.NewGuid()}.json", TestContext.Current.CancellationToken);

        Assert.Equal(["k1"], profile!.Keys.Select(key => key.KeyId));
        Assert.Empty(withoutKeys!.Keys);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}", ProfileProblem.Unreachable)]
    [InlineData(HttpStatusCode.OK, "not json", ProfileProblem.Malformed)]
    [InlineData(HttpStatusCode.OK, """{"ucp":{"capabilities":[]}}""", ProfileProblem.Malformed)]
    public async Task A_profile_that_cannot_be_used_says_why(HttpStatusCode status, string body, ProfileProblem expected)
    {
        var profiles = new HttpPlatformProfiles(new HttpClient(new Recording(status, body)), Options.Create(new UcpOptions { TrustedPlatformHosts = ["agent.example"] }), new TestClock());

        var (_, problem) = await profiles.GetAsync($"https://agent.example/{Guid.NewGuid()}.json", TestContext.Current.CancellationToken);

        Assert.Equal(expected, problem);
    }

    private sealed class Fixed(PlatformProfile? profile, ProfileProblem? problem) : IPlatformProfiles
    {
        public Task<(PlatformProfile? Profile, ProfileProblem? Problem)> GetAsync(string profileUrl, CancellationToken cancellationToken) =>
            Task.FromResult((profile, problem));
    }

    private sealed class Recording(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
