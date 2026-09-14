using System.Text.Json;
using Json.Schema;
using Xunit;

namespace ElGuerre.Tendero.Tests.Ucp;

/// <summary>
/// The specification's own JSON Schemas, loaded from disk and resolved against
/// each other with no network.
///
/// Every vendored file carries an absolute <c>$id</c> under
/// <c>https://ucp.dev/schemas/</c>, and the <c>$ref</c>s between them are
/// relative, so registering the whole set is all the resolution anybody needs.
/// Nothing here reaches the internet: a test that downloads the thing it
/// validates against changes its verdict when somebody else edits a file, which
/// is the opposite of a gate.
/// </summary>
internal static class UcpSchemas
{
    private static readonly Lazy<int> Registered = new(Load);

    /// <summary>
    /// A schema by its canonical id, ready to evaluate — for example
    /// <c>https://ucp.dev/schemas/profile.json#/$defs/business_schema</c>.
    ///
    /// **It is a one-line wrapper carrying a <c>$ref</c>, and that is not
    /// decoration.** <c>SchemaRegistry.Get</c> resolves a DOCUMENT by its base
    /// URI and ignores the fragment, so asking it for
    /// <c>profile.json#/$defs/business_schema</c> hands back the whole of
    /// <c>profile.json</c> — whose root is <c>allOf: [base]</c> and requires
    /// only that a <c>ucp</c> member exists. Every document with a <c>ucp</c>
    /// key passes that.
    ///
    /// Which is exactly what happened. The first run reported the profile as
    /// valid AND a profile with no <c>payment_handlers</c> as valid, and the
    /// negative control is the only reason that was caught rather than
    /// published as evidence. A <c>$ref</c> goes through the registry's own
    /// resolution, fragment included.
    /// </summary>
    public static JsonSchema Get(string id)
    {
        _ = Registered.Value;

        // The document has to be registered; the fragment is the $ref's job.
        Assert.NotNull(SchemaRegistry.Global.Get(new Uri(id.Split('#')[0])));

        return JsonSchema.FromText("{\"$ref\": \"" + id + "\"}");
    }

    /// <summary>
    /// Evaluate and, on failure, say WHERE. The default output is a boolean,
    /// which is useless in a test: "the profile is invalid" sends a person
    /// reading a 116-file schema set by hand.
    /// </summary>
    public static void Validate(string schemaId, JsonElement instance, string what)
    {
        var results = Get(schemaId).Evaluate(instance, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });

        if (results.IsValid)
            return;

        var errors = (results.Details ?? [])
            .Where(detail => detail.Errors is { Count: > 0 })
            .SelectMany(detail => detail.Errors!.Select(error =>
                $"  {detail.InstanceLocation} ({detail.EvaluationPath}): {error.Value}"))
            .Distinct()
            .Order()
            .ToArray();

        Assert.Fail(
            $"{what} does not satisfy {schemaId}:{Environment.NewLine}{Environment.NewLine}"
            + string.Join(Environment.NewLine, errors));
    }

    private static int Load()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "ucp-schemas");

        Assert.True(Directory.Exists(root),
            $"The vendored UCP schemas are missing from {root}. They are committed under "
            + "tests/Ucp.Tests/ucp-schemas and copied to the output by the .csproj.");

        var count = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var schema = JsonSchema.FromFile(file);

            if (schema.BaseUri is null)
                continue;

            SchemaRegistry.Global.Register(schema);
            count++;
        }

        // The whole point is that resolution is offline, so an empty or
        // half-loaded registry has to fail loudly rather than let every
        // validation pass over nothing.
        Assert.True(count > 100, $"Only {count} UCP schemas loaded from {root}; 116 are committed.");

        return count;
    }
}
