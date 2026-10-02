using ElGuerre.Tendero.AI.Chat;
using Microsoft.Extensions.AI;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>The contract against the Scripted kind: it runs in CI, where the local
/// model's run cannot (S-3b).</summary>
public sealed class ScriptedProfileContractTests : ChatProfileContractTests
{
    protected override IChatClient? Client(TimeSpan timeout) =>
        ScriptedChatClient.FromFile(Path.Combine(AppContext.BaseDirectory, "scripts", "contract.json"), timeout);
}
