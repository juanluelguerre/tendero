using SkiaSharp;

namespace ElGuerre.Tendero.Search.Images;

/// <summary>
/// A detector that answers from a script, for the browser specs and every machine
/// with no model, as the scripted speech engine is. It behaves as a real one
/// would where the contract asks it to: it refuses what is not a picture, names
/// only the kinds asked for, and clips a scripted box to the picture it was given.
/// </summary>
public sealed class ScriptedObjectDetector(params IReadOnlyList<ScriptedObject> script) : IObjectDetector
{
    public const string Key = "scripted";

    public string Name => Key;

    public Task<IReadOnlyList<DetectedObject>> DetectAsync(ReadOnlyMemory<byte> picture, IReadOnlyList<ObjectKind> kinds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (width, height) = SizeOf(picture);
        var asked = kinds.ToDictionary(kind => kind.Code);
        IReadOnlyList<DetectedObject> found =
        [
            .. from scripted in script
               where asked.ContainsKey(scripted.Code)
               let box = scripted.Box.ClippedTo(width, height)
               where box is not null
               select new DetectedObject(asked[scripted.Code], box.Value, scripted.Score),
        ];
        return Task.FromResult(found);
    }

    /// <summary>The picture's size from its header; the pixels are never decoded.</summary>
    private static (int Width, int Height) SizeOf(ReadOnlyMemory<byte> picture)
    {
        using var data = SKData.CreateCopy(picture.Span);
        using var codec = SKCodec.Create(data) ?? throw new ArgumentException("The bytes are not a picture.", nameof(picture));
        return (codec.Info.Width, codec.Info.Height);
    }
}

/// <summary>One line of a detector's script: a kind's code, where, and how sure.</summary>
public sealed record ScriptedObject(string Code, PictureBox Box, double Score);
