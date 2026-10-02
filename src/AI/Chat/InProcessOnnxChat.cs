using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace ElGuerre.Tendero.AI.Chat;

/// <summary>
/// A generative model run inside the process by ONNX Runtime GenAI, from a
/// folder the maker published (Phi-4-mini's official ONNX, P10-0) — no server,
/// no container. The extractors run over it exactly as they run over llama.cpp.
/// </summary>
public static class InProcessOnnxChat
{
    public static IChatClient Create(string modelDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        return new OnnxGenerationChatClient(modelDirectory);
    }
}

/// <summary>
/// Our own <see cref="IChatClient"/> over ONNX Runtime GenAI's generator, and not
/// the one the package ships, because of one line in it.
///
/// When a JSON-schema constraint completes, <c>GenerateNextToken()</c> ends the
/// generation <em>without appending a token</em>: P10-0 measured sequence
/// lengths of 645, 646, 647, 647. The package's <c>OnnxRuntimeGenAIChatClient</c>
/// (0.17.0) decodes "the last token of the sequence" after every step, so it
/// emits the closing brace twice and every schema-held answer fails to parse.
/// Here a token is decoded only when the sequence grew. Reported upstream:
/// microsoft/onnxruntime-genai#2633; this class goes when a fixed release does.
/// </summary>
internal sealed class OnnxGenerationChatClient : IChatClient
{
    private readonly Model model;
    private readonly Tokenizer tokenizer;
    private readonly Lock gate = new();

    public OnnxGenerationChatClient(string modelDirectory)
    {
        model = new Model(modelDirectory);
        tokenizer = new Tokenizer(model);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // One generation at a time: the model holds its own memory, and the
        // extractors ask one product at a time anyway.
        lock (gate)
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Generate(messages, options, cancellationToken))));
    }

    private string Generate(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
    {
        var conversation = JsonSerializer.Serialize(messages.Select(message => new Turn(message.Role.Value, message.Text)), TurnJson.Default.IEnumerableTurn);
        using var prompt = tokenizer.Encode(tokenizer.ApplyChatTemplate(null, conversation, null, true));

        using var parameters = new GeneratorParams(model);
        // The model's context is its ceiling by default, and its KV cache is
        // reserved for all of it up front: Phi-4-mini's 131,072 tokens are ~16 GB.
        parameters.SetSearchOption("max_length", prompt[0].Length + (options?.MaxOutputTokens ?? 1024));
        if (options?.Temperature is float temperature)
            parameters.SetSearchOption("temperature", temperature);
        if (options?.Seed is long seed)
            parameters.SetSearchOption("random_seed", seed);
        if (options?.ResponseFormat is ChatResponseFormatJson { Schema: { } schema })
            parameters.SetGuidance("json_schema", schema.GetRawText());

        using var generator = new Generator(model, parameters);
        generator.AppendTokenSequences(prompt);

        using var stream = tokenizer.CreateStream();
        var text = new StringBuilder();
        var length = generator.GetSequence(0).Length;

        while (!generator.IsDone())
        {
            cancellationToken.ThrowIfCancellationRequested();
            generator.GenerateNextToken();

            var sequence = generator.GetSequence(0);
            if (sequence.Length == length)
                break; // the step that ends a constrained generation appends nothing

            length = sequence.Length;
            text.Append(stream.Decode(sequence[^1]));
        }

        return text.ToString();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        yield return new ChatResponseUpdate(ChatRole.Assistant, response.Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        tokenizer.Dispose();
        model.Dispose();
    }
}

internal sealed record Turn(string role, string content);

[System.Text.Json.Serialization.JsonSerializable(typeof(IEnumerable<Turn>))]
internal sealed partial class TurnJson : System.Text.Json.Serialization.JsonSerializerContext;
