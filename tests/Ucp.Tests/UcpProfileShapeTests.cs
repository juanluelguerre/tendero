using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace ElGuerre.Tendero.Ucp.Tests;

/// <summary>
/// The manifest has the field names the specification uses, whatever the
/// serializer thinks.
///
/// **Today no field would come out wrong**, and that is worth saying rather than
/// dressing up: every member of the profile as it stands is one word, so
/// camelCase and the specification agree by accident. The accident ends in phase
/// 11 — <c>payment_handlers</c> and <c>signing_keys</c> are snake_case, the rest
/// of this API answers in camelCase, and a naming policy would rename them into
/// valid, well-formed JSON that no agent reading the specification can use. No
/// exception, no warning, and nothing else in the repository that notices.
///
/// So the names are written out now, while it costs nothing, and
/// <see cref="Every_member_names_itself"/> is what keeps it true for the field
/// somebody adds later. The text assertions below run with a camelCase policy
/// switched on, so the attributes have to win rather than merely agree.
/// </summary>
public sealed class UcpProfileShapeTests
{
    private static readonly JsonSerializerOptions Hostile = new()
    {
        // Exactly what the rest of the API does, and exactly what must not
        // reach this document.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static UcpProfile Sample => new()
    {
        Ucp = new UcpDocument
        {
            Version = UcpSpecification.Version,
            Services = new Dictionary<string, IReadOnlyList<UcpService>>
            {
                [UcpSpecification.ShoppingService] =
                [
                    new UcpService
                    {
                        Version = UcpSpecification.Version,
                        Spec = UcpSpecification.BaseUrl,
                        Transport = "mcp",
                        Endpoint = "https://shop.example/mcp",
                        Schema = "https://shop.example/openapi/tendero.json"
                    }
                ]
            },
            Capabilities = new Dictionary<string, UcpCapabilityDeclaration>
            {
                ["dev.ucp.shopping.catalog.search"] = new()
                {
                    Version = UcpSpecification.Version,
                    Spec = $"{UcpSpecification.BaseUrl}/catalog/",
                    Schema = "https://shop.example/openapi/tendero.json"
                }
            }
        }
    };

    [Fact]
    public void It_serialises_with_the_specifications_own_field_names()
    {
        var json = JsonSerializer.Serialize(Sample, Hostile);

        Assert.Contains("\"ucp\":", json, StringComparison.Ordinal);
        Assert.Contains("\"version\":", json, StringComparison.Ordinal);
        Assert.Contains("\"services\":", json, StringComparison.Ordinal);
        Assert.Contains("\"capabilities\":", json, StringComparison.Ordinal);
        Assert.Contains("\"transport\":", json, StringComparison.Ordinal);
        Assert.Contains("\"endpoint\":", json, StringComparison.Ordinal);
        Assert.Contains("\"schema\":", json, StringComparison.Ordinal);
        Assert.Contains("\"dev.ucp.shopping.catalog.search\":", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// A capability that extends nothing and configures nothing says nothing:
    /// the keys are absent, not null. Agents branch on a key existing, and
    /// <c>"extends": null</c> is a different document from one without the key.
    /// </summary>
    [Fact]
    public void Unused_optional_members_are_absent_rather_than_null()
    {
        var json = JsonSerializer.Serialize(Sample, Hostile);

        Assert.DoesNotContain("extends", json, StringComparison.Ordinal);
        Assert.DoesNotContain("config", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the round trip, because a document only this process can read is not
    /// a manifest.
    /// </summary>
    [Fact]
    public void It_reads_back_as_what_was_written()
    {
        var json = JsonSerializer.Serialize(Sample, Hostile);
        var back = JsonSerializer.Deserialize<UcpProfile>(json, Hostile);

        Assert.NotNull(back);
        Assert.Equal(UcpSpecification.Version, back.Ucp.Version);
        Assert.Equal("mcp", back.Ucp.Services[UcpSpecification.ShoppingService][0].Transport);
        Assert.True(back.Ucp.Capabilities.ContainsKey("dev.ucp.shopping.catalog.search"));
    }

    /// <summary>
    /// The rule rather than today's instance of it: every member of every record
    /// in the profile carries an explicit name.
    ///
    /// This is the one that matters, because the assertions above pass for a
    /// reason that stops being true — one-word members. A field added in phase 11
    /// without an attribute is spelled by whatever policy is in force, and this
    /// is the only thing that would say so.
    /// </summary>
    [Fact]
    public void Every_member_names_itself()
    {
        Type[] documents =
        [
            typeof(UcpProfile), typeof(UcpDocument),
            typeof(UcpService), typeof(UcpCapabilityDeclaration)
        ];

        var unnamed = documents
            .SelectMany(document => document.GetProperties()
                .Where(property =>
                    property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), false).Length == 0)
                .Select(property => $"{document.Name}.{property.Name}"))
            .Order()
            .ToArray();

        Assert.True(unnamed.Length == 0,
            "These carry no [JsonPropertyName]: "
            + string.Join(", ", unnamed)
            + ". UCP is snake_case and this API answers in camelCase, so a member "
            + "without an explicit name is spelled by whatever policy is in force "
            + "— valid JSON, unreadable manifest.");
    }

    /// <summary>
    /// The version is pinned, not tracked. A shop that advertised "whatever the
    /// latest is" would be promising a document nobody has read it against.
    /// </summary>
    [Fact]
    public void The_specification_version_is_the_one_this_was_written_against()
    {
        Assert.Equal("2026-04-08", UcpSpecification.Version);
        Assert.Equal("/.well-known/ucp", UcpSpecification.WellKnownPath);
    }
}
