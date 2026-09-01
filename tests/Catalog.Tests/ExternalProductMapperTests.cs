using Tendero.Catalog.Connectors;
using Tendero.Catalog.Domain;
using Xunit;

namespace Tendero.Catalog.Tests.Connectors;

/// <summary>
/// El mapeo de origen a agregado tiene dos consumidores que no pueden llamarse
/// entre ellos: el slice de importación y la puerta de calidad de búsqueda.
/// Mientras estuvo escrito dos veces, lo único que garantizaba que coincidieran
/// era un comentario. Esto es lo que lo garantiza ahora.
/// </summary>
public sealed class ExternalProductMapperTests
{
    private static ExternalProduct AnExternalProduct(decimal price = 29.90m) => new(
        ExternalId: "B073WXYZ01",
        Names: new Dictionary<string, string> { ["es"] = "Cafetera", ["en"] = "Coffee maker" },
        Descriptions: new Dictionary<string, string> { ["es"] = "De goteo" },
        Brand: "Moka",
        Category: "COFFEE_MAKER",
        PriceAmount: price,
        PriceCurrency: "EUR",
        Images: [],
        Attributes: new Dictionary<string, string> { ["color"] = "negro" });

    [Fact]
    public void A_new_product_carries_everything_the_source_gave()
    {
        var product = AnExternalProduct().ToNewProduct("seed");

        Assert.Equal("Cafetera", product.Name.In("es"));
        Assert.Equal("Coffee maker", product.Name.In("en"));
        Assert.Equal("De goteo", product.Description?.In("es"));
        Assert.Equal("Moka", product.Brand);
        Assert.Equal("COFFEE_MAKER", product.Category);
        Assert.Equal(29.90m, product.Price.Amount);
        Assert.Equal("negro", product.Attributes["color"]);
    }

    [Fact]
    public void A_new_product_is_linked_to_its_source_and_left_in_draft()
    {
        // Importar nunca publica: Draft es la razón de ser de la cola de
        // revisión, y sólo PublishProduct mueve un producto a Active (ADR 0012).
        var product = AnExternalProduct().ToNewProduct("seed");

        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Contains(new ExternalReference("seed", "B073WXYZ01"), product.ExternalReferences);
    }

    [Fact]
    public void Reimporting_updates_the_product_without_unpublishing_it()
    {
        // El origen manda sobre lo que el origen posee: textos, precio, atributos.
        // No sobre el estado — que un proveedor cambie una descripción no puede
        // devolver a la cola de revisión algo que ya estaba publicado.
        var product = AnExternalProduct().ToNewProduct("seed");
        product.Publish();

        AnExternalProduct(price: 34.50m).ApplyTo(product);

        Assert.Equal(34.50m, product.Price.Amount);
        Assert.Equal(ProductStatus.Active, product.Status);
    }

    [Fact]
    public void Reimporting_twice_does_not_duplicate_the_external_reference()
    {
        var external = AnExternalProduct();
        var product = external.ToNewProduct("seed");

        external.ApplyTo(product);
        product.LinkExternal("seed", external.ExternalId);

        Assert.Single(product.ExternalReferences);
    }
}
