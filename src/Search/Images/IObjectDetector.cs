namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// Where the things in a picture are, and what kind each is (the camera search
/// spec, CS-2): the port a photograph of a desk stands on, so that the laptop,
/// the lamp and the bottle on it are three questions and not one blur of
/// likeness.
///
/// **Every detector behind this**, as every speech engine is behind
/// <c>ISpeechToText</c> (ADR 0061): the detector is measured before it is chosen,
/// so each candidate is one class and one keyed registration, inherits
/// <c>ObjectDetectorContractTests</c>, and loads its model from its options.
///
/// It answers what it found, raw: one box a kind, the floor and the count are
/// <see cref="ObjectsInPicture"/>'s, the same over every detector.
/// </summary>
public interface IObjectDetector
{
    /// <summary>The detector's stable name — <c>owlv2</c>, <c>scripted</c> — for the span and the measurement.</summary>
    string Name { get; }

    /// <summary>Whether this process was started with a detector at all. False is not a failure: the whole picture is then the one object (CS-9).</summary>
    bool IsConfigured => true;

    /// <summary>
    /// The objects of the kinds asked for, in the picture's own pixels after its
    /// orientation, each with a score from 0 to 1. Only the kinds given are ever
    /// named: they are the shop's categories and the common objects it does not
    /// sell, so a detector is never asked about anything the shop cannot answer.
    /// </summary>
    /// <exception cref="ArgumentException">The bytes are not a picture this can decode.</exception>
    Task<IReadOnlyList<DetectedObject>> DetectAsync(ReadOnlyMemory<byte> picture, IReadOnlyList<ObjectKind> kinds, CancellationToken cancellationToken = default);
}

/// <summary>
/// A kind a detector is asked to look for.
/// </summary>
/// <param name="Code">The shop's category code (<c>DESK_LAMP</c>) or the name of a common object it does not sell (<c>bottle</c>).</param>
/// <param name="Words">What the detector reads, in English: an open-vocabulary detector looks for words. Alternatives are joined by <c>or</c> (<c>bed or chest of drawers</c>).</param>
/// <param name="Sold">Whether the shop sells this kind, which is what lets it say so when it does not.</param>
public sealed record ObjectKind(string Code, string Words, bool Sold)
{
    /// <summary>
    /// Each alternative on its own. A model that reads language takes the words
    /// whole; a detector that matches words with pixels asks each alternative and
    /// keeps the best — as one query, "bed or chest of drawers" matched neither
    /// well (2026-10-19, OWLv2 on ABO).
    /// </summary>
    public IReadOnlyList<string> Alternatives => this.Words.Split(" or ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>A rectangle of a picture, in its pixels, from its top left corner.</summary>
public readonly record struct PictureBox(int Left, int Top, int Width, int Height)
{
    /// <summary>The part of this box inside a picture of that size, or null when none of it is.</summary>
    public PictureBox? ClippedTo(int pictureWidth, int pictureHeight)
    {
        var left = Math.Max(0, this.Left);
        var top = Math.Max(0, this.Top);
        var right = Math.Min(pictureWidth, this.Left + this.Width);
        var bottom = Math.Min(pictureHeight, this.Top + this.Height);
        return right > left && bottom > top ? new PictureBox(left, top, right - left, bottom - top) : null;
    }
}

/// <summary>One thing a detector found: what kind, where, and how sure.</summary>
public sealed record DetectedObject(ObjectKind Kind, PictureBox Box, double Score);

/// <summary>The detector of a process started without one: never asked, and it says so.</summary>
public sealed class NoObjectDetector : IObjectDetector
{
    public string Name => "none";

    public bool IsConfigured => false;

    public Task<IReadOnlyList<DetectedObject>> DetectAsync(ReadOnlyMemory<byte> picture, IReadOnlyList<ObjectKind> kinds, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No object detector is configured; ask IsConfigured first.");
}
