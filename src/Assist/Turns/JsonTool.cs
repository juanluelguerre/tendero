using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.Assist.Turns;

/// <summary>
/// A function whose schema is given, not inferred: what the contexts declared is
/// exactly what the model is shown. A model's mistake — arguments a read or a
/// proposal refuses — is its answer, with the reason, so the model can ask again
/// (ADR 0054); any other failure is the shop's, and the function loop gives the
/// model only that it failed. What both of the shop's agents build their tools from.
/// </summary>
internal sealed class JsonTool(
    string name, string description, JsonElement schema, Func<JsonElement, CancellationToken, Task<object?>> invoke) : AIFunction
{
    public override string Name => name;

    public override string Description => description;

    public override JsonElement JsonSchema => schema;

    /// <summary>The characters the tools add to every call — names, descriptions,
    /// schemas — which a turn counts against the window before it asks.</summary>
    public static int Size(IEnumerable<AIFunction> tools) =>
        tools.Sum(tool => tool.Name.Length + tool.Description.Length + tool.JsonSchema.GetRawText().Length);

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await invoke(JsonSerializer.SerializeToElement(
                arguments.ToDictionary(pair => pair.Key, pair => pair.Value), ArgumentSchemas.Json), cancellationToken);
        }
        catch (ArgumentException mistake)
        {
            return JsonSerializer.SerializeToElement(new { error = mistake.Message }, ArgumentSchemas.Json);
        }
    }
}
