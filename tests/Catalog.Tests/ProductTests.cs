using Tendero.Catalog.Domain;
using Tendero.SharedKernel;
using Xunit;

namespace Tendero.Catalog.Tests.Domain;

/// <summary>
/// Las dos reglas del agregado que se calculaban fuera de él, cada una con su
/// propia versión: cuál es la foto de portada y cómo se escribe un slug.
/// </summary>
public sealed class ProductTests
{
    private static Product AProduct(string name = "Cafetera") =>
        Product.Create(LocalizedText.From("es", name), new Money(29.90m, "EUR"));

    [Fact]
    public void The_cover_photo_is_the_one_with_the_lowest_sort_order()
    {
        // La lista guarda el orden de inserción, así que "la primera de la lista"
        // y "la de menor SortOrder" coinciden hasta que dejan de hacerlo. El
        // listado del backoffice ordenaba y el documento de búsqueda no, de modo
        // que el mismo producto podía enseñar dos fotos distintas.
        var product = AProduct();
        product.AddImage(new ImageId("aaa"));
        product.AddImage(new ImageId("bbb"));

        Assert.Equal(new ImageId("aaa"), product.PrimaryImage?.Id);
        Assert.Equal(0, product.PrimaryImage?.SortOrder);
    }

    [Fact]
    public void A_product_with_no_images_has_no_cover_photo() =>
        Assert.Null(AProduct().PrimaryImage);

    [Fact]
    public void Adding_the_same_image_twice_does_not_duplicate_it()
    {
        var product = AProduct();
        product.AddImage(new ImageId("aaa"));
        product.AddImage(new ImageId("aaa"));

        Assert.Single(product.Images);
    }

    [Theory]
    // Partir sólo por espacios dejaba paréntesis y tildes dentro de la URL.
    [InlineData("Cafetera Espresso (12 tazas)", "cafetera-espresso-12-tazas")]
    [InlineData("Zapatillas de running — mujer", "zapatillas-de-running-mujer")]
    [InlineData("Mochila 25L", "mochila-25l")]
    [InlineData("  espacios   de   sobra  ", "espacios-de-sobra")]
    public void A_slug_only_carries_letters_digits_and_hyphens(string name, string expected) =>
        Assert.Equal(expected, AProduct(name).Slug.In("es"));

    [Theory]
    [InlineData("Café con leche ñiño", "cafe-con-leche-nino")]
    [InlineData("Ánfora Gütiérrez", "anfora-gutierrez")]
    public void Accents_are_folded_rather_than_escaped(string name, string expected)
    {
        // Se pliegan con una tabla explícita, no con Normalize(FormD): el repo
        // compila con InvariantGlobalization=true, donde la normalización Unicode
        // devuelve la cadena intacta sin lanzar. Este test es lo que lo demostró.
        Assert.Equal(expected, AProduct(name).Slug.In("es"));
    }

    [Fact]
    public void Every_culture_gets_its_own_slug()
    {
        var product = Product.Create(
            new LocalizedText(new Dictionary<string, string>
            {
                ["es"] = "Cafetera de goteo",
                ["en"] = "Drip coffee maker"
            }),
            new Money(29.90m, "EUR"));

        Assert.Equal("cafetera-de-goteo", product.Slug.In("es"));
        Assert.Equal("drip-coffee-maker", product.Slug.In("en"));
    }

    [Fact]
    public void Creating_a_product_is_one_event_not_two()
    {
        // Marca y categoría entran en Create. Cuando llegaban en un UpdateDetails
        // posterior, dar de alta un producto emitía dos ProductUpserted, y el
        // worker de indexación escribía dos veces el mismo documento.
        var product = Product.Create(
            LocalizedText.From("es", "Cafetera"),
            new Money(29.90m, "EUR"),
            description: null,
            brand: "Moka",
            category: "COFFEE_MAKER");

        Assert.Equal("Moka", product.Brand);
        Assert.Equal("COFFEE_MAKER", product.Category);
        Assert.Single(product.DomainEvents);
    }
}
