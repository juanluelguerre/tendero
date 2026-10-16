using ElGuerre.Tendero.AI.Speech.Parakeet;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests.Speech;

/// <summary>
/// Parakeet's greedy TDT loop, as transformers 5.9.0 runs it
/// (<c>ParakeetTDTGenerationMixin</c>): at each encoder frame the joint network
/// gives a token and a duration; a token that is not blank is emitted and fed to
/// the predictor; the frame advances by the duration, and by one when a blank
/// would not move; never more than <c>max_symbols_per_step</c> tokens on one frame.
/// The networks are fakes here: what is tested is the loop, not the model.
/// </summary>
public sealed class TdtGreedyTests
{
    private const int Blank = 4;
    private static readonly int[] Durations = [0, 1, 2, 3, 4];

    /// <summary>A joint network that answers from a script, frame by frame: what it would say on each call.</summary>
    private sealed class ScriptedNetworks(params (int Token, int Duration)[] answers) : ITdtNetworks
    {
        private int call;
        public List<int> Fed { get; } = [];
        public List<int> FramesAsked { get; } = [];

        public int VocabularySize => 5;

        public TdtState Start() => new([0f], [0f], [0f]);

        public TdtState Predict(int token, TdtState state)
        {
            Fed.Add(token);
            return state;
        }

        public float[] Joint(TdtState state, int frame)
        {
            FramesAsked.Add(frame);
            var (token, duration) = call < answers.Length ? answers[call] : (Blank, 1);
            call++;
            var logits = new float[VocabularySize + Durations.Length];
            logits[token] = 1;
            logits[VocabularySize + duration] = 1;
            return logits;
        }
    }

    private static int[] Decode(ScriptedNetworks networks, int frames, int maxSymbolsPerStep = 10) =>
        TdtGreedy.Decode(networks, frames, Blank, Durations, maxSymbolsPerStep);

    [Fact]
    public void Tokens_are_emitted_and_fed_to_the_predictor_and_the_frame_moves_by_their_duration()
    {
        var networks = new ScriptedNetworks((1, 2), (2, 1), (Blank, 1));

        var tokens = Decode(networks, frames: 4);

        Assert.Equal([1, 2], tokens);
        Assert.Equal([Blank, 1, 2], networks.Fed);
        Assert.Equal([0, 2, 3], networks.FramesAsked);
    }

    /// <summary>A blank that says "stay" would never end: it moves one frame.</summary>
    [Fact]
    public void A_blank_with_no_duration_still_moves_one_frame_and_feeds_nothing()
    {
        var networks = new ScriptedNetworks((Blank, 0), (Blank, 0), (3, 1));

        var tokens = Decode(networks, frames: 3);

        Assert.Equal([3], tokens);
        Assert.Equal([0, 1, 2], networks.FramesAsked);
        Assert.Equal([Blank, 3], networks.Fed);
    }

    /// <summary>Several tokens on one frame (duration 0), and the ceiling that keeps a frame from emitting for ever.</summary>
    [Fact]
    public void Tokens_with_no_duration_stay_on_the_frame_up_to_the_ceiling()
    {
        var networks = new ScriptedNetworks((1, 0), (2, 0), (3, 0), (Blank, 1));

        var tokens = Decode(networks, frames: 2, maxSymbolsPerStep: 2);

        Assert.Equal([1, 2, 3], tokens);
        Assert.Equal([0, 0, 1, 1], networks.FramesAsked);
    }

    [Fact]
    public void Nothing_but_blanks_is_no_token_at_all() =>
        Assert.Empty(Decode(new ScriptedNetworks(), frames: 5));
}
