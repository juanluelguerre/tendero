using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// Narrows the document's numeric types down to numbers.
///
/// The ASP.NET Core generator emits <c>"type": ["number", "string"]</c> for
/// <c>decimal</c>, <c>double</c> and <c>int</c>, with a <c>pattern</c> alongside.
/// It describes what the deserialiser ACCEPTS — System.Text.Json can read
/// <c>"79.95"</c> as well as <c>79.95</c> — and applied to a response it is
/// simply false: the API never serialises a price as a string.
///
/// It is not cosmetic. Without it the generated types come out as
/// <c>string | number</c> and the error surfaces in the wrong place: `formatPrice`
/// stops compiling in the frontend because of an imprecision in the document, and
/// the tempting way out is to loosen the client until it swallows both — which is
/// exactly how a contract stops meaning anything.
///
/// The union is kept where it is true: input parameters arrive from the query
/// string and everything there is text, so only the component schemas are
/// touched, never the parameter ones.
/// </summary>
internal sealed class NumbersAreNumbersTransformer : IOpenApiSchemaTransformer
{
    private const JsonSchemaType Numeric = JsonSchemaType.Number | JsonSchemaType.Integer;

    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Type is not { } type)
            return Task.CompletedTask;

        // Only when the union is "number or string": a pure string stays as it is.
        if ((type & JsonSchemaType.String) == 0 || (type & Numeric) == 0)
            return Task.CompletedTask;

        schema.Type = type & ~JsonSchemaType.String;

        // The pattern existed to validate the string variant. Without it, it
        // describes a constraint on something that can no longer arrive.
        schema.Pattern = null;

        return Task.CompletedTask;
    }
}
