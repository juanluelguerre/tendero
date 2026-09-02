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
/// The attribute definitions, with their label in EVERY culture.
///
/// In every culture on purpose, against what the other endpoints do: what is
/// being reviewed here is precisely whether a translation is missing, so
/// returning text already resolved into one culture would hide the very fact the
/// screen exists for. Same reasoning as `missingCultures` in the review queue,
/// taken one step further.
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
        // Cultures missing from the definition or from ANY of its options: an
        // untranslated label on an option is exactly what kept "navy blue shoes"
        // from matching, so counting only at definition level would leave out the
        // case that matters.
        [
            .. Cultures.Where(culture =>
                !definition.Label.Cultures.Contains(culture) ||
                definition.Options.Any(option => !option.Label.Cultures.Contains(culture)))
        ]);
}
