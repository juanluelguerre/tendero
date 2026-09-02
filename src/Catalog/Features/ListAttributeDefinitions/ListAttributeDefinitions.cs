using System.Diagnostics;
using Carter;
using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace ElGuerre.Tendero.Catalog.Features.ListAttributeDefinitions;

/// <summary>
/// Las definiciones de atributo, con su etiqueta en TODAS las culturas.
///
/// Y en todas a propósito, en contra de lo que hacen los demás endpoints: aquí
/// lo que se revisa es precisamente si falta una traducción, así que devolver el
/// texto ya resuelto en una cultura escondería el dato por el que existe la
/// pantalla. Es el mismo razonamiento que `missingCultures` en la cola de
/// revisión, llevado un paso más allá.
/// </summary>
public sealed record ListAttributeDefinitionsQuery : IQuery<ListAttributeDefinitionsResult>;

public sealed record AttributeOptionView(string Code, IReadOnlyDictionary<string, string> Label);

public sealed record AttributeDefinitionView(
    string Code,
    IReadOnlyDictionary<string, string> Label,
    string Kind,
    string? Unit,
    bool IsVariantAxis,
    bool IsFacet,
    bool IsSearchable,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<AttributeOptionView> Options,
    IReadOnlyList<string> MissingCultures);

public sealed record ListAttributeDefinitionsResult(IReadOnlyList<AttributeDefinitionView> Items);

public sealed class ListAttributeDefinitionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/catalog/attributes",
            async Task<Ok<ListAttributeDefinitionsResult>> (
                   IQueryDispatcher dispatcher, CancellationToken ct) =>
                TypedResults.Ok(await dispatcher.SendAsync(new ListAttributeDefinitionsQuery(), ct)))
            .RequireAuthorization(TenderoPolicyNames.Shopkeeper)
            .WithTags("Catalog")
            .WithName("ListAttributeDefinitions");
    }
}

public sealed class ListAttributeDefinitionsHandler(IAttributeDefinitionReader definitions)
    : IQueryHandler<ListAttributeDefinitionsQuery, ListAttributeDefinitionsResult>
{
    private static readonly ActivitySource Telemetry = new(TelemetrySources.Catalog);
    private static readonly string[] Cultures = ["es", "en"];

    public async Task<ListAttributeDefinitionsResult> HandleAsync(
        ListAttributeDefinitionsQuery query, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("catalog.list_attributes");

        var all = await definitions.AllAsync(cancellationToken);
        activity?.SetTag("catalog.attribute_count", all.All.Count);

        return new ListAttributeDefinitionsResult(
        [
            .. all.All
                .OrderBy(definition => definition.Code, StringComparer.Ordinal)
                .Select(View)
        ]);
    }

    private static AttributeDefinitionView View(AttributeDefinition definition) => new(
        definition.Code,
        definition.Label.Values,
        definition.Kind.ToString(),
        definition.Unit,
        definition.IsVariantAxis,
        definition.IsFacet,
        definition.IsSearchable,
        definition.Aliases,
        [.. definition.Options.Select(option => new AttributeOptionView(option.Code, option.Label.Values))],
        // Le faltan culturas a la definición o a CUALQUIERA de sus opciones: una
        // etiqueta sin traducir en una opción es exactamente lo que hacía que
        // "navy blue shoes" no casara, así que contarla sólo a nivel de
        // definición dejaría fuera el caso que importa.
        [
            .. Cultures.Where(culture =>
                !definition.Label.Cultures.Contains(culture) ||
                definition.Options.Any(option => !option.Label.Cultures.Contains(culture)))
        ]);
}
