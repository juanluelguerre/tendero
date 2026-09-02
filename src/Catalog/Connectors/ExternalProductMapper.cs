using ElGuerre.Tendero.Catalog.Domain;
using ElGuerre.Tendero.Catalog.Ports;

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
    public static Product ToNewProduct(
        this ExternalProduct external, string source, TimeProvider clock,
        AttributeDefinitions? definitions = null)
    {
        var product = Product.Create(
            clock,
            external.LocalizedName,
            external.Price,
            external.LocalizedDescription,
            external.Brand,
            external.Category);

        product.LinkExternal(clock, source, external.ExternalId);
        CopyAttributes(external, product, clock, definitions);
        EnsureDefaultVariant(external, product, clock);

        return product;
    }

    /// <summary>
    /// Reimportación: el origen manda sobre lo que el origen posee. No toca
    /// estado — un producto ya publicado no vuelve a Draft porque su proveedor
    /// haya cambiado una descripción (ADR 0012).
    /// </summary>
    public static void ApplyTo(
        this ExternalProduct external, Product product, TimeProvider clock,
        AttributeDefinitions? definitions = null)
    {
        product.UpdateDetails(
            clock, external.LocalizedName, external.LocalizedDescription, external.Brand, external.Category);
        product.SetPrice(clock, external.Price);
        CopyAttributes(external, product, clock, definitions);
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

    /// <summary>
    /// Traduce lo que manda el origen a valores tipados, resolviendo contra las
    /// definiciones del catálogo. Es aquí donde "azul marino" deja de ser el
    /// dato y pasa a ser la opción <c>NAVY_BLUE</c>, que sabe decirse en inglés.
    ///
    /// Sin definiciones —SearchEval antes de sembrarlas, un test— cae a texto
    /// plano, que es exactamente el comportamiento anterior. Degradar a lo que
    /// ya había es mejor que fallar: la importación no debería depender de que
    /// alguien haya definido los atributos primero.
    /// </summary>
    private static void CopyAttributes(
        ExternalProduct external, Product product, TimeProvider clock, AttributeDefinitions? definitions)
    {
        foreach (var (name, value) in external.Attributes)
            product.SetAttribute(clock, Resolve(name, value, definitions));
    }

    internal static AttributeValue Resolve(string name, string value, AttributeDefinitions? definitions)
    {
        var definition = definitions?.ForSourceKey(name);
        if (definition is null)
            return AttributeValue.Plain(name, value);

        return definition.Kind switch
        {
            AttributeKind.Option => definition.ResolveOption(value) is { } option
                ? AttributeValue.Option(definition.Code, option.Code)
                // El origen mandó un valor que la definición no conoce. Se
                // guarda tal cual en vez de descartarlo: perder el dato sería
                // peor, y así queda visible para quien revise.
                : AttributeValue.Plain(definition.Code, value),

            AttributeKind.Number => decimal.TryParse(
                value.TrimEnd('"'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? AttributeValue.Numeric(definition.Code, number)
                    : AttributeValue.Plain(definition.Code, value),

            AttributeKind.Boolean => AttributeValue.Boolean(
                definition.Code,
                value.Trim() is "si" or "sí" or "yes" or "true" or "1"),

            _ => AttributeValue.Plain(definition.Code, value)
        };
    }
}
