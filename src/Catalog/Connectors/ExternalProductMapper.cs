using ElGuerre.Tendero.Catalog.Domain;

namespace ElGuerre.Tendero.Catalog.Connectors;

/// <summary>
/// Convierte lo que entrega un conector en el agregado del catálogo. Existe como
/// pieza propia porque tiene DOS consumidores y ninguno puede llamar al otro: el
/// slice <c>ImportProducts</c> y la puerta de calidad <c>tools/SearchEval</c>,
/// que indexa el catálogo semilla sin pasar por Postgres.
///
/// Mientras estuvo escrito dos veces, el comentario de SearchEval decía "mismos
/// pasos y mismo orden que ImportProductsHandler" — que es la forma educada de
/// decir que la puerta mediría otro sistema en cuanto la importación cambiase.
///
/// Vive en <c>Connectors</c> y no en <c>Domain</c> a propósito: el dominio sólo
/// puede ver el SharedKernel (invariante 4), así que es el contrato de entrada
/// quien conoce al agregado, nunca al revés. El test de arquitectura lo comprueba.
///
/// Las imágenes NO entran aquí: ingerirlas es E/S contra el almacén, y quien la
/// hace es el slice. La evaluación no las necesita porque no son campo buscable.
/// </summary>
public static class ExternalProductMapper
{
    /// <summary>Alta: producto nuevo en Draft, ya enlazado a su origen.</summary>
    public static Product ToNewProduct(this ExternalProduct external, string source, TimeProvider clock)
    {
        var product = Product.Create(
            clock,
            external.LocalizedName,
            external.Price,
            external.LocalizedDescription,
            external.Brand,
            external.Category);

        product.LinkExternal(clock, source, external.ExternalId);
        CopyAttributes(external, product, clock);
        EnsureDefaultVariant(external, product, clock);

        return product;
    }

    /// <summary>
    /// Reimportación: el origen manda sobre lo que el origen posee. No toca
    /// estado — un producto ya publicado no vuelve a Draft porque su proveedor
    /// haya cambiado una descripción (ADR 0012).
    /// </summary>
    public static void ApplyTo(this ExternalProduct external, Product product, TimeProvider clock)
    {
        product.UpdateDetails(
            clock, external.LocalizedName, external.LocalizedDescription, external.Brand, external.Category);
        product.SetPrice(clock, external.Price);
        CopyAttributes(external, product, clock);
        EnsureDefaultVariant(external, product, clock);
    }

    /// <summary>
    /// Todo lo comprable es una variante, también lo que llega sin ninguna.
    ///
    /// Un origen que no distingue tallas ni colores describe un producto con una
    /// sola forma de comprarse, y llamarla "variante por defecto" es más honesto
    /// que dejar el carrito y la línea de pedido con dos caminos —uno con
    /// variante y otro sin— que habría que mantener en paralelo para siempre.
    ///
    /// El SKU se deriva del id externo, así que reimportar no crea una segunda:
    /// <c>AddVariant</c> es idempotente por SKU.
    /// </summary>
    private static void EnsureDefaultVariant(ExternalProduct external, Product product, TimeProvider clock)
    {
        if (product.Variants.Count > 0)
        {
            // Reimportación: el origen manda sobre el precio, como en el resto
            // del producto.
            product.SetVariantPrice(clock, DefaultSku(external), external.Price);
            return;
        }

        product.AddVariant(clock, DefaultSku(external), external.Price);
    }

    private static string DefaultSku(ExternalProduct external) => $"{external.ExternalId}-DEFAULT";

    private static void CopyAttributes(ExternalProduct external, Product product, TimeProvider clock)
    {
        foreach (var (name, value) in external.Attributes)
            product.SetAttribute(clock, name, value);
    }
}
