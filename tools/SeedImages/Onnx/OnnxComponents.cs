using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ElGuerre.Tendero.SeedImages;

/// <summary>
/// Opening one component of the export at a time.
///
/// **One at a time is the design, not an accident.** In fp16 the UNet is about
/// five gigabytes, the two text towers about 1.6 and the decoder 0.2 — nearly
/// seven before a single activation exists, on a card with eight. The generator
/// therefore runs in phases: encode every prompt, close the towers, open the
/// UNet, walk every picture, close it, open the decoder. Opening a five gigabyte
/// session costs real seconds, so it is paid once per batch rather than once per
/// image.
/// </summary>
public static class OnnxSession
{
    public static InferenceSession Open(string modelPath, string provider)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };

        if (provider == "dml")
        {
            // The memory pattern planner over-reserves on a graph whose shapes are
            // free, and this one's are all -1.
            options.EnableMemoryPattern = false;
            options.AppendExecutionProvider_DML(0);
        }
        else
        {
            options.AppendExecutionProvider_CPU();
        }

        if (!File.Exists(modelPath))
            throw new InvalidOperationException(
                $"""
                 No model at '{modelPath}'.

                 This tool needs an SDXL export in the diffusers layout, optimised
                 for DirectML. Download one and point --models at the folder that
                 contains text_encoder/, text_encoder_2/, unet/ and vae_decoder/.
                 See tools/SeedImages/README.md.
                 """);

        return new InferenceSession(modelPath, options);
    }
}

/// <summary>
/// One of SDXL's text towers over ONNX Runtime.
///
/// Two things here were read off the real graph rather than assumed, and neither
/// is guessable: the first tower takes its token ids as **Int32** and the second
/// as **Int64**, and the layer that matters is the **penultimate** one —
/// `hidden_states.11` of thirteen, `hidden_states.31` of thirty-three — not the
/// last. Using the last is not an error; it is a slightly worse image with no
/// other symptom.
/// </summary>
public sealed class OnnxTextTower(InferenceSession session, int width, string hiddenLayer, bool longIds)
    : ITextTower
{
    private const string PooledName = "text_embeds";

    public int Width => width;

    public TowerOutput Encode(int[] tokenIds)
    {
        var inputName = session.InputMetadata.Keys.First();

        NamedOnnxValue input = longIds
            ? NamedOnnxValue.CreateFromTensor(
                inputName, new DenseTensor<long>(tokenIds.Select(id => (long)id).ToArray().AsMemory(), [1, tokenIds.Length]))
            : NamedOnnxValue.CreateFromTensor(
                inputName, new DenseTensor<int>(tokenIds.AsMemory(), [1, tokenIds.Length]));

        using var results = session.Run([input]);

        var hidden = results.First(value => value.Name == hiddenLayer)
            .AsTensor<float>().ToArray();

        var pooled = results.FirstOrDefault(value => value.Name == PooledName)?
            .AsTensor<float>().ToArray();

        return new TowerOutput(hidden, pooled);
    }

    public void Dispose() => session.Dispose();
}

/// <summary>
/// The UNet over ONNX Runtime. Five inputs, and their names came from the graph:
/// `sample`, `timestep`, `encoder_hidden_states`, `text_embeds`, `time_ids`.
/// </summary>
public sealed class OnnxUnet(InferenceSession session, int latentSize) : IUnet
{
    public float[] Predict(float[] sample, float timestep, TextConditioning text, float[] timeIds)
    {
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("sample",
                new DenseTensor<float>(sample.AsMemory(), [2, 4, latentSize, latentSize])),
            NamedOnnxValue.CreateFromTensor("timestep",
                new DenseTensor<float>(new[] { timestep }.AsMemory(), [1])),
            NamedOnnxValue.CreateFromTensor("encoder_hidden_states",
                new DenseTensor<float>(text.HiddenStates.AsMemory(), [2, text.Tokens, 2048])),
            NamedOnnxValue.CreateFromTensor("text_embeds",
                new DenseTensor<float>(text.PooledEmbeds.AsMemory(), [2, 1280])),
            NamedOnnxValue.CreateFromTensor("time_ids",
                new DenseTensor<float>(timeIds.AsMemory(), [2, 6]))
        };

        using var results = session.Run(inputs);

        return results.First().AsTensor<float>().ToArray();
    }

    public void Dispose() => session.Dispose();
}

/// <summary>
/// The decoder, which is the only step that produces anything a person could
/// look at. Everything before it happens on a 128×128 grid of four channels,
/// which is why the whole thing fits in eight gigabytes at all.
/// </summary>
public sealed class OnnxVaeDecoder(InferenceSession session, int latentSize) : IDisposable
{
    /// <summary>
    /// SDXL's latent scaling factor. **0.13025, and not the 0.18215 that every
    /// Stable Diffusion 1.5 snippet on the internet carries** — the wrong one
    /// gives a picture that is recognisable and washed out, which is the hardest
    /// kind of wrong to notice.
    /// </summary>
    private const float ScalingFactor = 0.13025f;

    /// <summary>Returns interleaved RGB bytes for one square image.</summary>
    public byte[] Decode(float[] latents, out int size)
    {
        var scaled = new float[latents.Length];

        for (var index = 0; index < latents.Length; index++)
            scaled[index] = latents[index] / ScalingFactor;

        var input = NamedOnnxValue.CreateFromTensor("latent_sample",
            new DenseTensor<float>(scaled.AsMemory(), [1, 4, latentSize, latentSize]));

        using var results = session.Run([input]);

        var tensor = results.First().AsTensor<float>();

        size = tensor.Dimensions[^1];

        return ToRgb(tensor.ToArray(), size);
    }

    /// <summary>
    /// Channels-first, roughly -1 to 1, into interleaved bytes. The clamp is not
    /// decoration: the decoder overshoots at hard edges, and an unclamped cast
    /// wraps a bright pixel round to black.
    /// </summary>
    private static byte[] ToRgb(float[] chw, int size)
    {
        var plane = size * size;
        var rgb = new byte[plane * 3];

        for (var pixel = 0; pixel < plane; pixel++)
            for (var channel = 0; channel < 3; channel++)
            {
                var value = ((chw[(channel * plane) + pixel] * 0.5f) + 0.5f) * 255f;
                rgb[(pixel * 3) + channel] = (byte)Math.Clamp(value, 0f, 255f);
            }

        return rgb;
    }

    public void Dispose() => session.Dispose();
}
