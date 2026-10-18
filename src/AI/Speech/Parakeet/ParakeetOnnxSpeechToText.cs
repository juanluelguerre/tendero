using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ElGuerre.Tendero.AI.Speech.Parakeet;

/// <summary>
/// NVIDIA's Parakeet-TDT-0.6B-v3 inside this process, on ONNX Runtime (phase 14;
/// the author's ruling of 2026-10-16: option A, CC-BY-4.0 accepted). The three
/// networks are our own export (<c>scripts/prepare-parakeet.py</c>, ADR 0031); the
/// features and the greedy loop are C# (<see cref="ParakeetFeatures"/>,
/// <see cref="TdtGreedy"/>), held to the maker's own numbers. Which revision it
/// loads is its folder, never code (ADR 0061).
/// </summary>
public sealed class ParakeetOnnxSpeechToText : ISpeechToText, IDisposable
{
    public const string Key = "parakeet-onnx";

    private const int Hidden = 640;
    private const int Layers = 2;
    private const int Blank = 8192;
    private const int MaxSymbolsPerStep = 10;
    private static readonly int[] Durations = [0, 1, 2, 3, 4];

    private readonly InferenceSession encoder;
    private readonly InferenceSession decoder;
    private readonly InferenceSession joint;
    private readonly ParakeetVocabulary vocabulary;

    public ParakeetOnnxSpeechToText(string directory)
    {
        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        encoder = new InferenceSession(Path.Combine(directory, "encoder.onnx"), options);
        decoder = new InferenceSession(Path.Combine(directory, "decoder.onnx"), options);
        joint = new InferenceSession(Path.Combine(directory, "joint.onnx"), options);
        vocabulary = ParakeetVocabulary.FromFile(Path.Combine(directory, "vocab.json"));
    }

    public string Name => Key;

    public bool IsConfigured => true;

    /// <summary>Parakeet hears its 25 European languages without being told which; the culture is not passed.</summary>
    public Task<Heard> TranscribeAsync(SpokenAudio audio, string culture, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            try
            {
                return Transcribe(audio, cancellationToken);
            }
            catch (OnnxRuntimeException failure)
            {
                throw new SpeechUnavailableException(Key, failure.Message);
            }
        }, cancellationToken);
    }

    private Heard Transcribe(SpokenAudio audio, CancellationToken cancellationToken)
    {
        var (values, frames, valid) = ParakeetFeatures.Of(audio.Samples());
        var mask = new bool[frames];
        for (var i = 0; i < valid; i++)
            mask[i] = true;

        using var encoded = encoder.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_features", new DenseTensor<float>(values, [1, frames, ParakeetFeatures.Mels])),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<bool>(mask, [1, frames])),
        ]);
        var steps = encoded.First().AsTensor<float>().ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var networks = new Networks(this, steps);
        var tokens = TdtGreedy.Decode(networks, Math.Min(EncodedSteps(valid), steps.Length / Hidden), Blank, Durations, MaxSymbolsPerStep);
        var words = vocabulary.Words(tokens);
        return words.Length == 0 ? Heard.Nothing : new Heard(words);
    }

    /// <summary>The encoder's valid steps for its valid frames: three convolutions of stride 2, kernel 3, padding 1.</summary>
    internal static int EncodedSteps(int frames)
    {
        for (var i = 0; i < 3; i++)
            frames = frames <= 0 ? 0 : (frames - 1) / 2 + 1;
        return frames;
    }

    public void Dispose()
    {
        encoder.Dispose();
        decoder.Dispose();
        joint.Dispose();
    }

    /// <summary>The predictor and the joint network over one utterance's encoded steps.</summary>
    private sealed class Networks(ParakeetOnnxSpeechToText model, float[] steps) : ITdtNetworks
    {
        public int VocabularySize => Blank + 1;

        public TdtState Start() => new(new float[Hidden], new float[Layers * Hidden], new float[Layers * Hidden]);

        public TdtState Predict(int token, TdtState state)
        {
            using var outputs = model.decoder.Run(
            [
                NamedOnnxValue.CreateFromTensor("token", new DenseTensor<long>(new long[] { token }, [1, 1])),
                NamedOnnxValue.CreateFromTensor("h", new DenseTensor<float>(state.Hidden, [Layers, 1, Hidden])),
                NamedOnnxValue.CreateFromTensor("c", new DenseTensor<float>(state.Cell, [Layers, 1, Hidden])),
            ]);
            var results = outputs.ToDictionary(output => output.Name, output => output.AsTensor<float>().ToArray());
            return new TdtState(results["decoder_out"], results["h_out"], results["c_out"]);
        }

        public float[] Joint(TdtState state, int frame)
        {
            var encoderFrame = new float[Hidden];
            Array.Copy(steps, frame * Hidden, encoderFrame, 0, Hidden);
            using var outputs = model.joint.Run(
            [
                NamedOnnxValue.CreateFromTensor("decoder_out", new DenseTensor<float>(state.DecoderOut, [1, Hidden])),
                NamedOnnxValue.CreateFromTensor("encoder_frame", new DenseTensor<float>(encoderFrame, [1, Hidden])),
            ]);
            return outputs.First().AsTensor<float>().ToArray();
        }
    }
}
