using ElGuerre.Tendero.Catalog.Connectors;
using ElGuerre.Tendero.SharedKernel;

namespace ElGuerre.Tendero.Catalog.Features.ImportProducts;

/// <summary>
/// A source's note as the import's answer says it (phase 16, P16-5; ADR 0059):
/// the code a front end composes its own sentence from, what it is about, and the
/// sentence in the language asked for whoever reads the answer as it is.
/// </summary>
internal static class ImportNotes
{
    public static Message<ImportNoteCode> Said(SourceNote note, bool english) =>
        new(note.Code, note.About, Sentences.StartingUpper(Sentence(note, english)));

    private static string Sentence(SourceNote note, bool english)
    {
        var about = note.About is null ? "" : $"{note.About}: ";
        var detail = note.Detail ?? "";

        return note.Code switch
        {
            ImportNoteCode.CategoryNotMapped => english
                ? $"{about}its category at the source, “{detail}”, has no category here; it was imported without one."
                : $"{about}su categoría en el origen, «{detail}», no tiene equivalente aquí; se ha importado sin categoría.",
            ImportNoteCode.LocaleIgnored => english
                ? $"The store publishes {detail}, a language this shop does not speak; those texts were not read."
                : $"La tienda publica {detail}, un idioma que esta tienda no habla; esos textos no se han leído.",
            ImportNoteCode.DescriptionHadLayout => english
                ? $"{about}its description had a table or a list; it was kept as plain lines."
                : $"{about}su descripción tenía una tabla o una lista; se ha guardado como líneas de texto.",
            ImportNoteCode.VariantWithoutSku => english
                ? $"{about}variant {detail} has no SKU at the source; it was given one here."
                : $"{about}la variante {detail} no tiene SKU en el origen; se le ha dado uno aquí.",
            ImportNoteCode.SkuSharedAtSource => english
                ? $"{about}several variants share the SKU {detail} at the source; each was given its own here."
                : $"{about}varias variantes comparten el SKU {detail} en el origen; cada una ha recibido uno propio aquí.",
            ImportNoteCode.SkuChangedAtSource => english
                ? $"{about}the source renamed a SKU ({detail}); the shop keeps its own, which stock and orders use."
                : $"{about}el origen ha renombrado un SKU ({detail}); la tienda conserva el suyo, que usan el stock y los pedidos.",
            ImportNoteCode.ImageUnreadable => english
                ? $"{about}a picture could not be read ({detail}); the product was imported without it."
                : $"{about}no se ha podido leer una imagen ({detail}); el producto se ha importado sin ella.",
            ImportNoteCode.CollectionNotMapped => english
                ? $"{about}the collection “{detail}” holds products that share no category here; its old address will not be redirected."
                : $"{about}la colección «{detail}» agrupa productos que no comparten ninguna categoría aquí; su dirección antigua no se redirigirá.",
        };
    }
}
