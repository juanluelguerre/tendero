using ElGuerre.Tendero.AI.Chat;
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>
/// The contract against Steward's local model, the steward-model container on its
/// pinned port. A CI runner cannot hold a 4B model, so with no container answering
/// the tests skip; they run locally with Tendero__Steward=true.
/// </summary>
public sealed class LlamaCppProfileContractTests : ChatProfileContractTests
{
    private static readonly Uri Server = new("http://localhost:38085/");

    private static readonly Lazy<bool> Running = new(() =>
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            return http.GetAsync(new Uri(Server, "health")).GetAwaiter().GetResult().IsSuccessStatusCode;
        }
        catch (Exception unreachable) when (unreachable is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    });

    protected override IChatClient? Client(TimeSpan timeout) =>
        Running.Value ? OpenAICompatibleChat.CreateAt(new Uri(Server, "v1/"), "qwen3.5-4b-q8_0", timeout: timeout) : null;
}
