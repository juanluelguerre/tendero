using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.AI.Tests;

/// <summary>
/// What a call says of itself for metering (ADR 0053): its operation, and when it has
/// them its conversation, prompt version and person — added to the options a caller
/// already has, never in place of them.
/// </summary>
public sealed class ModelCallTests
{
    [Fact]
    public void A_call_names_its_operation_and_only_what_it_was_given()
    {
        var options = ModelCall.Options("steward.turn");

        Assert.Equal("steward.turn", ModelCall.Read(options, ModelCall.Operation));
        Assert.Null(ModelCall.Read(options, ModelCall.Conversation));
        Assert.Null(ModelCall.Read(options, ModelCall.PromptVersion));
        Assert.Null(ModelCall.Read(options, ModelCall.Person));
    }

    [Fact]
    public void A_call_carries_its_conversation_its_prompt_version_and_its_person()
    {
        var options = ModelCall.Options("steward.turn", "conv-1", "steward-v1", person: "lucia");

        Assert.Equal(
            ("conv-1", "steward-v1", "lucia"),
            (ModelCall.Read(options, ModelCall.Conversation), ModelCall.Read(options, ModelCall.PromptVersion), ModelCall.Read(options, ModelCall.Person)));
    }

    /// <summary>A caller's own options are the ones returned: its temperature, its tools and what it had already said of the call.</summary>
    [Fact]
    public void A_callers_own_options_and_properties_are_kept()
    {
        var own = new ChatOptions { Temperature = 0.2f, AdditionalProperties = new() { ["mine"] = "kept" } };

        var options = ModelCall.Options("claims.extract", options: own);

        Assert.Same(own, options);
        Assert.Equal(0.2f, options.Temperature);
        Assert.Equal("kept", ModelCall.Read(options, "mine"));
        Assert.Equal("claims.extract", ModelCall.Read(options, ModelCall.Operation));
    }

    [Fact]
    public void Options_with_nothing_said_of_them_read_as_nothing()
    {
        Assert.Null(ModelCall.Read(null, ModelCall.Operation));
        Assert.Null(ModelCall.Read(new ChatOptions(), ModelCall.Operation));
    }
}
