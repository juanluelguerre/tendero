using System.Text.Json.Nodes;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// Declares a <see cref="WireEnum{TEnum}"/> query parameter as what it is: a
/// string that is one of the enum's names (ADR 0043).
///
/// Left to the generator, a type that binds through <c>TryParse</c> is a bare
/// <c>string</c>, which is true of the query string and says nothing about the
/// set — the closed-set test would have to take the parameter on trust, and the
/// frontend would type it as any string at all.
///
/// An OPERATION transformer, because the generator writes a parsable
/// parameter's schema directly and never passes it through the schema
/// transformers; the operation's description still knows the parameter's type.
/// </summary>
internal sealed class ClosedSetsInTheQueryTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var described in context.Description.ParameterDescriptions)
        {
            if (ClosedSetOf(described.Type) is not { } set)
                continue;

            var parameter = operation.Parameters?.OfType<OpenApiParameter>()
                .FirstOrDefault(parameter => parameter.Name == described.Name);
            if (parameter is null)
                continue;

            parameter.Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [.. Enum.GetNames(set).Select(name => (JsonNode)JsonValue.Create(name))],
            };
        }

        return Task.CompletedTask;
    }

    /// <summary>The enum behind a <c>WireEnum&lt;T&gt;</c> or <c>WireEnum&lt;T&gt;?</c>; null for anything else.</summary>
    private static Type? ClosedSetOf(Type? type)
    {
        if (type is null)
            return null;

        type = Nullable.GetUnderlyingType(type) ?? type;

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WireEnum<>)
            ? type.GetGenericArguments()[0]
            : null;
    }
}
