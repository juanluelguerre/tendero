using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace ElGuerre.Tendero.Knowledge.Chat;

/// <summary>
/// A generative model run inside the process by ONNX Runtime GenAI, from a
/// folder the maker published (Phi-4-mini's official ONNX, P10-0) — no server,
/// no container. Its <see cref="IChatClient"/> honours a JSON-schema
/// <c>ResponseFormat</c> with constrained decoding, so the extractors run over it
/// exactly as they run over llama.cpp.
/// </summary>
public static class InProcessOnnxChat
{
    public static IChatClient Create(string modelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        return new OnnxRuntimeGenAIChatClient(modelDirectory);
    }
}
