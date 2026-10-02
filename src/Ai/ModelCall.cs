using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.AI;

/// <summary>
/// What a call is for, said where it is made: the metering reads it from the
/// options, so a row says which operation, conversation and prompt it belonged to
/// without anything ambient.
/// </summary>
public static class ModelCall
{
    public const string Operation = "tendero.operation";
    public const string Conversation = "tendero.conversation";
    public const string PromptVersion = "tendero.prompt_version";

    public static ChatOptions Options(string operation, string? conversation = null, string? promptVersion = null, ChatOptions? options = null)
    {
        options ??= new ChatOptions();
        options.AdditionalProperties ??= [];
        options.AdditionalProperties[Operation] = operation;
        if (conversation is not null)
            options.AdditionalProperties[Conversation] = conversation;
        if (promptVersion is not null)
            options.AdditionalProperties[PromptVersion] = promptVersion;
        return options;
    }

    internal static string? Read(ChatOptions? options, string key) =>
        options?.AdditionalProperties?.TryGetValue(key, out var value) == true ? value?.ToString() : null;
}
