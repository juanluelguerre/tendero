using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// Declares every enum as the strings it travels as (ADR 0043).
///
/// The generator declares an enum's names only when the converter is the
/// framework's own <c>JsonStringEnumConverter</c>. The API's converter
/// (<c>WireJson.Enums</c>) delegates to it and adds one refusal — a
/// combination such as <c>"Day, Week"</c> is not a member — and a converter the
/// generator does not recognise is documented as <c>{}</c>, anything at all.
/// This says again what the wire already does, so the document keeps every set.
/// </summary>
internal sealed class EnumsAreTheirNamesTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        var underlying = Nullable.GetUnderlyingType(type);
        var set = underlying ?? type;
        if (!set.IsEnum)
            return Task.CompletedTask;

        // A nullable set lists null among its values, as the generator writes it
        // for the framework's converter. JSON Schema's enum admits null; the
        // OpenAPI model types its list as non-nullable nodes, and a null entry
        // is how it writes one, hence the forgiving operator.
        IEnumerable<JsonNode> names = Enum.GetNames(set).Select(name => (JsonNode)JsonValue.Create(name));
        schema.Type = null;
        schema.Enum = [.. underlying is null ? names : names.Append(null!)];

        return Task.CompletedTask;
    }
}
