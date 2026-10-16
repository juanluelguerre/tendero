namespace ElGuerre.Tendero.AI.Speech.Parakeet;

/// <summary>The predictor's state between tokens: its output and the LSTM's hidden and cell states.</summary>
public sealed record TdtState(float[] DecoderOut, float[] Hidden, float[] Cell);

/// <summary>
/// The two networks the TDT loop asks at each step, over an utterance already
/// encoded. An interface so the loop is tested with fakes, and so the ONNX
/// sessions behind it are one adapter of it.
/// </summary>
public interface ITdtNetworks
{
    /// <summary>Tokens the joint network scores; the durations come after them in the same logits.</summary>
    int VocabularySize { get; }

    /// <summary>The predictor before any token.</summary>
    TdtState Start();

    /// <summary>The predictor after one more token.</summary>
    TdtState Predict(int token, TdtState state);

    /// <summary>The joint network's logits for the predictor's state at one encoder frame: tokens, then durations.</summary>
    float[] Joint(TdtState state, int frame);
}

/// <summary>
/// Parakeet's greedy Token-and-Duration Transducer decoding, as transformers 5.9.0
/// runs it (<c>ParakeetTDTGenerationMixin</c>): the joint network gives a token and
/// a duration at each frame; a token that is not blank is emitted and fed to the
/// predictor; the frame advances by the duration, by one when a blank would not
/// move, and by one when a frame has emitted the most it may.
/// </summary>
public static class TdtGreedy
{
    public static int[] Decode(
        ITdtNetworks networks, int frames, int blank, IReadOnlyList<int> durations, int maxSymbolsPerStep)
    {
        var tokens = new List<int>();
        var state = networks.Predict(blank, networks.Start());
        var emittedHere = 0;

        for (var frame = 0; frame < frames;)
        {
            var logits = networks.Joint(state, frame);
            var token = ArgMax(logits, 0, networks.VocabularySize);
            var duration = durations[ArgMax(logits, networks.VocabularySize, durations.Count)];

            if (token != blank)
            {
                tokens.Add(token);
                state = networks.Predict(token, state);
                emittedHere++;
            }

            if (duration == 0 && (token == blank || emittedHere >= maxSymbolsPerStep))
                duration = 1;
            if (duration > 0)
                emittedHere = 0;
            frame += duration;
        }

        return [.. tokens];
    }

    private static int ArgMax(float[] values, int start, int count)
    {
        var best = 0;
        for (var i = 1; i < count; i++)
            if (values[start + i] > values[start + best])
                best = i;
        return best;
    }
}
