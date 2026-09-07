namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// Whatever draws the pictures.
///
/// **A port with keyed adapters, which is ADR 0003 applied to one more external
/// system.** A diffusion model running in this process and a hosted one behind an
/// HTTP call are the same kind of thing from the catalogue's point of view: text
/// goes in and pixels come out. Everything downstream — the safe area, the
/// repainted background, the 1024px WebP, the vision pass — is shared, which is
/// the whole reason for drawing the line here rather than lower down.
///
/// The port is at the level of a BATCH and not of one image, because the two
/// adapters differ exactly there. SDXL runs in phases dictated by eight
/// gigabytes of card: encode every prompt, close the towers, open the UNet, walk
/// every picture. A hosted model is one call per image and has no such shape.
/// A per-image port would have forced the local one to reopen a five gigabyte
/// session ninety-two times.
/// </summary>
public interface IIllustrationGenerator : IDisposable
{
    /// <summary>For the report, so a picture can be traced to what drew it.</summary>
    string Name { get; }

    /// <summary>
    /// Whether the same product drawn twice gives the same picture twice.
    ///
    /// **Declared rather than assumed, because the two adapters differ and it
    /// matters.** The local one seeds its noise from the `item_id`, so redrawing
    /// number 47 is a repeatable act. A hosted model exposes no seed, so
    /// redrawing it is a new roll — which is a property of the tool a person
    /// should be told about before they rely on it, not after.
    /// </summary>
    bool IsDeterministic { get; }

    IReadOnlyList<GeneratedImage> Draw(
        IReadOnlyList<(string ItemId, string Prompt)> jobs, string negative, Action<string> say);
}

/// <summary>
/// The tail every adapter shares: measure, inset, repaint, encode.
///
/// It lives outside both of them on purpose. The guarantees this tool actually
/// makes — the object inside the central 75%, the background exactly #FAF9F7,
/// a WebP under 120 KB with no alpha — are arithmetic, and arithmetic should not
/// be reimplemented once per model.
/// </summary>
public static class Finished
{
    public static GeneratedImage From(string itemId, byte[] rgb, int size, TimeSpan took)
    {
        var safeArea = SafeAreaProbe.Probe(rgb, size);
        var bounds = SafeAreaProbe.Measure(rgb, size);
        var webp = ImageNormaliser.ToWebp(rgb, size, out var quality);

        var background =
            $"#{bounds.Background.Red:X2}{bounds.Background.Green:X2}{bounds.Background.Blue:X2}";

        return new GeneratedImage(itemId, webp, quality, safeArea, background, took);
    }
}
