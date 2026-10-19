using ElGuerre.Tendero.Search.Contracts;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// The ways the shop says no to a question asked with a picture, each with its
/// code (ADR 0059). Shared by every slice a picture reaches — searching with
/// one, its objects, words against the pictures — so the same picture refused
/// by two of them is refused with one code and one sentence.
/// </summary>
public static class PictureRefusals
{
    public static readonly Refusal PictureNotTaken = new(
        "PictureNotTaken", StatusCodes.Status400BadRequest, "La foto no se puede usar.", "The picture cannot be used.");

    public static readonly Refusal NotAvailable = new(
        "VisualSearchNotAvailable", StatusCodes.Status503ServiceUnavailable,
        "Aquí no se busca con imágenes.", "Searching by picture is not available here.");

    public static readonly Refusal NotAnswering = new(
        "VisualSearchNotAnswering", StatusCodes.Status503ServiceUnavailable,
        "La búsqueda por imagen no responde ahora.", "Searching by picture is not answering right now.");

    /// <summary>A picture whose bytes say they are one but do not decode: refused as the others are, at the door's code.</summary>
    public static LocalizedText Unreadable { get; } = LocalizedText.Both(
        "The picture could not be read. Try another, or a JPEG of it.",
        "No hemos podido leer la foto. Prueba con otra, o con un JPEG de ella.");

    public static ProblemHttpResult Problem(Refusal refusal, string es, string en) =>
        TypedResults.Problem(statusCode: refusal.Status, extensions: refusal.Said(es: es, en: en));

    /// <summary>The picture refused, with why in both languages.</summary>
    public static ProblemHttpResult NotTaken(LocalizedText why) =>
        Problem(PictureNotTaken, why.In("es"), why.In("en"));

    /// <summary>
    /// Visual search missing or failing, said without the failure's detail: that
    /// names a store and a port, is logged whole, and says nothing to a shopper.
    /// </summary>
    public static ProblemHttpResult NotAnsweringFor(Exception exception) =>
        exception is SearchStageNotConfiguredException
            ? Problem(NotAvailable,
                "Esta tienda se ha arrancado sin búsqueda por imagen. La búsqueda por palabras funciona como siempre.",
                "This shop was started without visual search. Searching by words works as always.")
            : Problem(NotAnswering,
                "La búsqueda por palabras funciona como siempre. Prueba la foto de nuevo dentro de un momento.",
                "Searching by words works as always. Try the picture again in a moment.");
}

/// <summary>A picture whose header reads as one over a body that does not decode.</summary>
public sealed class UnreadablePictureException(Exception inner)
    : Exception("The picture could not be decoded.", inner);
