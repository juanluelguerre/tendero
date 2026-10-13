using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ElGuerre.Tendero.Api;

/// <summary>
/// Declares money as it travels (phase 13b): an integer in the currency's minor
/// unit and the currency beside it.
///
/// The wire's converter (<c>WireJson.Money</c>) writes
/// <c>{"amount":11390,"currency":"EUR"}</c>, and a converter the generator does
/// not recognise is documented from the type's own members — a number with a
/// fraction and a string, which is exactly the shape this phase leaves behind.
/// This says again what the wire does, so the front ends' types and an agent
/// reading the document are told the truth.
/// </summary>
internal sealed class MoneyIsMinorUnitsTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (type != typeof(Money))
            return Task.CompletedTask;

        schema.Type = JsonSchemaType.Object;
        schema.Description = "Money: a whole number of the currency's minor unit (ISO 4217) and the currency. 113.90 EUR is 11390.";
        schema.Properties = new Dictionary<string, IOpenApiSchema>
        {
            // The range a JavaScript reader holds exactly, which is the
            // converter's own (MinorUnits.Largest): a client's generator is
            // told a plain number is enough, and is right.
            ["amount"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Integer,
                Format = "int64",
                Minimum = (-MinorUnits.Largest).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Maximum = MinorUnits.Largest.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            ["currency"] = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "^[A-Z]{3}$" },
        };
        schema.Required = new HashSet<string> { "amount", "currency" };
        schema.AdditionalPropertiesAllowed = false;

        return Task.CompletedTask;
    }
}
