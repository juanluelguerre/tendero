namespace ElGuerre.Tendero.SharedKernel;

/// <summary>
/// **What the shop says beside an answer** (the wire's spec, phase 13b): a
/// <paramref name="Code"/> from a closed set, what it is <paramref name="About"/>,
/// and the sentence. One type for it, wherever an answer explains itself.
///
/// Two readers, one message. A front end composes its own sentence from the
/// code and the answer's data, formatted for its reader — its money in its
/// culture, its emphasis, a link. An agent with no front end reads
/// <paramref name="Text"/>, which the shop wrote in the language asked. The
/// code ties the two wordings: they cannot be about different things.
/// </summary>
/// <param name="About">What in the answer it concerns — a slot, a line, a requirement — or null when it is the answer as a whole.</param>
public sealed record Message<TCode>(TCode Code, string? About, string Text)
    where TCode : struct, Enum;
