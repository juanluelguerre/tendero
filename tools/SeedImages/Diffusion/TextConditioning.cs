namespace ElGuerre.Tendero.SeedImages;

/// <summary>What one text tower answers for one prompt.</summary>
/// <param name="Hidden">The penultimate layer, flattened: tokens × width.</param>
/// <param name="Pooled">The projected sentence embedding, 1280 wide. Only the
/// second tower's is used; the first tower's is discarded.</param>
public sealed record TowerOutput(float[] Hidden, float[]? Pooled);

/// <summary>
/// One of SDXL's two text towers.
///
/// It is an interface so the arrangement below can be tested with deterministic
/// fakes: which tower fills which channels, and which pooled embedding survives,
/// are exactly the mistakes that produce a correctly-shaped tensor meaning
/// something else.
/// </summary>
public interface ITextTower : IDisposable
{
    /// <summary>768 for the first tower, 1280 for the second. They add up to the
    /// 2048 the UNet's cross-attention expects.</summary>
    int Width { get; }

    TowerOutput Encode(int[] tokenIds);
}

/// <summary>
/// The text half of what the UNet is told, batched for guidance.
/// </summary>
/// <param name="HiddenStates">[2, tokens, 2048] flattened — the negative prompt
/// first, the positive second.</param>
/// <param name="PooledEmbeds">[2, 1280] flattened, in the same order.</param>
/// <param name="Tokens">How many tokens each row carries.</param>
public sealed record TextConditioning(float[] HiddenStates, float[] PooledEmbeds, int Tokens)
{
    /// <summary>
    /// The two prompts as one tower sees them. **Per tower and not shared**: the
    /// two tokenizers pad with different tokens — the end marker in the first,
    /// `!` in the second — so one set of ids fed to both is wrong for one of
    /// them, silently, in every image.
    /// </summary>
    public readonly record struct PromptTokens(int[] Negative, int[] Positive);

    private const int PooledWidth = 1280;

    /// <summary>
    /// Encodes both prompts through both towers and lays them out the way the
    /// UNet wants them.
    ///
    /// Three decisions live here and none of them is obvious from the tensor
    /// shapes:
    ///
    /// **The towers are concatenated along their channels**, first tower low and
    /// second tower high, because that is the order the cross-attention weights
    /// were trained against. Swapping them yields a tensor of exactly the right
    /// size that means something else, and the symptom is images that ignore the
    /// prompt slightly.
    ///
    /// **The pooled embedding comes from the second tower only.** The first tower
    /// has one too and it is thrown away. Both are 1280 wide, so a shape check
    /// would not notice the substitution.
    ///
    /// **The negative prompt is row zero.** Guidance takes the difference between
    /// the rows, so the wrong order does not fail — it steers towards everything
    /// the negative prompt existed to keep out.
    /// </summary>
    public static TextConditioning Build(
        ITextTower first, PromptTokens firstTokens, ITextTower second, PromptTokens secondTokens)
    {
        var tokens = firstTokens.Positive.Length;

        var lowNegative = first.Encode(firstTokens.Negative);
        var lowPositive = first.Encode(firstTokens.Positive);
        var highNegative = second.Encode(secondTokens.Negative);
        var highPositive = second.Encode(secondTokens.Positive);

        var width = first.Width + second.Width;
        var hidden = new float[2 * tokens * width];

        Interleave(hidden, row: 0, tokens, first.Width, lowNegative.Hidden, highNegative.Hidden, second.Width);
        Interleave(hidden, row: 1, tokens, first.Width, lowPositive.Hidden, highPositive.Hidden, second.Width);

        var pooled = new float[2 * PooledWidth];

        (highNegative.Pooled ?? new float[PooledWidth]).AsSpan(0, PooledWidth).CopyTo(pooled.AsSpan(0));
        (highPositive.Pooled ?? new float[PooledWidth]).AsSpan(0, PooledWidth).CopyTo(pooled.AsSpan(PooledWidth));

        return new TextConditioning(hidden, pooled, tokens);
    }

    /// <summary>
    /// Writes one row of the batch: for every token, the first tower's channels
    /// then the second tower's, back to back.
    /// </summary>
    private static void Interleave(
        float[] destination, int row, int tokens, int lowWidth, float[] low, float[] high, int highWidth)
    {
        var width = lowWidth + highWidth;
        var start = row * tokens * width;

        for (var token = 0; token < tokens; token++)
        {
            var at = start + (token * width);

            low.AsSpan(token * lowWidth, lowWidth).CopyTo(destination.AsSpan(at, lowWidth));
            high.AsSpan(token * highWidth, highWidth).CopyTo(destination.AsSpan(at + lowWidth, highWidth));
        }
    }
}

/// <summary>
/// SDXL's micro-conditioning: six numbers saying what size the source image was,
/// what crop it came from, and what size is being asked for.
///
/// **All of them are the size the model actually produces, not the size we want
/// the file to be.** SDXL was trained conditioned on its source resolution, so
/// telling it 1400 asks for something it never saw; the 1400 is a resize that
/// happens after the picture exists, and it is SkiaSharp's business.
///
/// Inside the UNet each value becomes a 256-wide embedding, giving 1536, which is
/// concatenated with the 1280 pooled text embedding to make the 2816 the model's
/// class-embedding projection expects. That arithmetic checking out is a good
/// sign the three numbers are the right ones.
/// </summary>
public static class TimeIds
{
    public static float[] For(int size, int batch)
    {
        float[] row = [size, size, 0f, 0f, size, size];

        var ids = new float[batch * row.Length];

        for (var index = 0; index < batch; index++)
            row.CopyTo(ids, index * row.Length);

        return ids;
    }
}
