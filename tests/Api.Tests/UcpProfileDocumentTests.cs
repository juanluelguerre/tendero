using System.Text.Json;
using System.Text.Json.Nodes;
using ElGuerre.Tendero.Ucp;
using Xunit;

namespace ElGuerre.Tendero.Api.Tests;

/// <summary>
/// The committed UCP profile has to be the one the API serves.
///
/// **Verify was declined, and this is why.** `docs/testing.md` has prescribed it
/// for exactly this document since before there was a document, and the roadmap
/// says a derived manifest is only safe if a change shows up in a diff — which
/// is right, and is already how this repository handles its other derived
/// document. `docs/openapi/tendero.json` is a committed snapshot compared by a
/// test with an environment variable to regenerate it. Bringing in a second
/// snapshot mechanism for the second derived document would mean two workflows,
/// two file layouts and two things to explain, to gain nothing this one does not
/// already do. Same call as Respawn, for the same reason: the tool is good and
/// the need is already met.
///
/// The document itself is the point. It is composed from the capability registry
/// on every request, so it cannot describe a shop this process is not — but
/// "derived" is a claim about the mechanism, and the diff is what makes a change
/// to the SHAPE visible to a person before an agent reads it.
/// </summary>
public sealed class UcpProfileDocumentTests
{
    private const string UpdateVariable = "UPDATE_UCP";

    private static readonly JsonSerializerOptions Formatting = new() { WriteIndented = true };

    [Fact]
    public async Task The_committed_profile_is_the_one_the_api_serves()
    {
        var ct = TestContext.Current.CancellationToken;

        var served = await FetchProfileAsync(ct);
        var committed = CommittedProfilePath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) is not (null or ""))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(committed)!);
            await File.WriteAllTextAsync(committed, Serialise(served), ct);
            return;
        }

        Assert.True(File.Exists(committed),
            $"{committed} does not exist. Generate it with: {UpdateVariable}=1 dotnet test");

        var onDisk = JsonNode.Parse(await File.ReadAllTextAsync(committed, ct));

        Assert.True(
            JsonNode.DeepEquals(onDisk, served),
            $"""
             The UCP profile served at /.well-known/ucp differs from {committed}.

             Something about what this shop advertises changed — a capability, a
             service binding, a version. That is exactly the change a person should
             see before an agent does. Regenerate it and read the diff:

                 {UpdateVariable}=1 dotnet test --project tests/Api.Tests
             """);
    }

    /// <summary>
    /// The rules a BUSINESS profile has to satisfy, read from the
    /// specification's own JSON Schema rather than from its prose — which said
    /// <c>signing_keys</c> where the schema says <c>keys</c>, and did not
    /// mention that <c>payment_handlers</c> is required of a merchant.
    ///
    /// It is not schema validation. Validating properly would take a JSON Schema
    /// library and that is a dependency decision, not a coding one; these are
    /// the four rules that would actually catch a mistake here.
    /// </summary>
    [Fact]
    public async Task It_satisfies_the_business_profiles_own_rules()
    {
        var profile = await FetchProfileAsync(TestContext.Current.CancellationToken);
        var ucp = profile?["ucp"];

        Assert.NotNull(ucp);

        // ucp.json#/$defs/base requires version; business_schema requires
        // services and payment_handlers.
        Assert.NotNull(ucp!["version"]);
        Assert.NotNull(ucp["services"]);
        Assert.NotNull(ucp["payment_handlers"]);

        // service.json business_schema: entity requires version, base requires
        // transport, and an mcp binding requires an endpoint.
        var binding = ucp["services"]!["dev.ucp.shopping"]!.AsArray()[0]!;
        Assert.NotNull(binding["version"]);
        Assert.Equal("mcp", binding["transport"]!.GetValue<string>());
        Assert.NotNull(binding["endpoint"]);

        // capability.json business_schema: entity requires version, and the
        // merchant schema adds schema — the URL a platform fetches.
        foreach (var capability in ucp["capabilities"]!.AsObject())
        {
            var declaration = capability.Value!.AsArray()[0]!;

            Assert.NotNull(declaration["version"]);
            // Under the version: the unversioned address answers 404 (found 2026-10-13).
            Assert.StartsWith(
                $"https://ucp.dev/{UcpSpecification.Version}/schemas/",
                declaration["schema"]!.GetValue<string>(),
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Public with no token, because the specification requires it rather than
    /// because we chose it: discovery that needs a credential is not discovery.
    /// </summary>
    [Fact]
    public async Task It_is_readable_without_a_token()
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/ucp", TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonNode?> FetchProfileAsync(CancellationToken ct)
    {
        using var factory = new TenderoApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/ucp", ct);
        response.EnsureSuccessStatusCode();

        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
    }

    private static string Serialise(JsonNode? profile) =>
        profile?.ToJsonString(Formatting) + Environment.NewLine;

    private static string CommittedProfilePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tendero.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "docs", "ucp", "well-known-ucp.json");
    }
}
