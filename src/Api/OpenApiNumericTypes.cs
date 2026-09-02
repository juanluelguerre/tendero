using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// Estrecha los tipos numéricos del documento a números.
///
/// El generador de ASP.NET Core emite <c>"type": ["number", "string"]</c> para
/// <c>decimal</c>, <c>double</c> e <c>int</c>, con un <c>pattern</c> al lado.
/// Describe lo que el deserializador ACEPTA — System.Text.Json puede leer
/// <c>"79.95"</c> además de <c>79.95</c> — y aplicado a una respuesta es
/// sencillamente falso: la API nunca serializa un precio como cadena.
///
/// No es cosmético. Sin esto los tipos generados salen como
/// <c>string | number</c> y el error aparece donde no toca: `formatPrice` deja
/// de compilar en el frontend por una imprecisión del documento, y la salida
/// tentadora es relajar el cliente para que trague ambos, que es exactamente
/// como un contrato deja de significar algo.
///
/// La unión se conserva donde sí es cierta: los parámetros de entrada llegan de
/// la query string y ahí todo es texto, así que sólo se tocan los esquemas de
/// componentes, no los de parámetros.
/// </summary>
internal sealed class NumbersAreNumbersTransformer : IOpenApiSchemaTransformer
{
    private const JsonSchemaType Numeric = JsonSchemaType.Number | JsonSchemaType.Integer;

    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Type is not { } type)
            return Task.CompletedTask;

        // Sólo cuando la unión es "número o cadena": un string puro se queda como está.
        if ((type & JsonSchemaType.String) == 0 || (type & Numeric) == 0)
            return Task.CompletedTask;

        schema.Type = type & ~JsonSchemaType.String;

        // El pattern existía para validar la variante en cadena. Sin ella, describe
        // una restricción sobre algo que ya no puede llegar.
        schema.Pattern = null;

        return Task.CompletedTask;
    }
}
