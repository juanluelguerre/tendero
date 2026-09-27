using ElGuerre.Tendero.Knowledge.Extraction;
using Microsoft.Extensions.AI;
using Xunit;

namespace ElGuerre.Tendero.Knowledge.Tests;

/// <summary>
/// What the extractor asks of a model and what it makes of the answer — with a
/// fake <see cref="IChatClient"/>, so no model is needed: the request is the
/// contract the P10-0 numbers were measured under.
/// </summary>
public sealed class ChatClaimExtractorTests
{
    private static readonly AttributeSpec[] Attributes =
    [
        new("DROP_MM", "Number", "mm", "drop", []),
        new("MATERIAL", "Option", null, "material", ["CAST_IRON", "LINEN"]),
    ];

    [Fact]
    public async Task It_asks_for_a_schema_held_answer_at_temperature_zero_with_a_seed_and_a_ceiling()
    {
        var chat = new FakeChat("""{"claims":[]}""");

        await Extract(chat);

        var options = chat.Options!;
        Assert.Equal(0, options.Temperature);
        Assert.Equal(42, options.Seed);
        Assert.Equal(ChatClaimExtractor.MaxTokens, options.MaxOutputTokens);
        var format = Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
        Assert.NotNull(format.Schema);
    }

    /// <summary>
    /// The whole schema, as the model is held to it. It is part of the prompt: a
    /// change here changes what every claim was read under, so it bumps
    /// <see cref="ChatClaimExtractor.PromptVersion"/> — and this test is where
    /// that change is noticed.
    /// </summary>
    [Fact]
    public void The_schema_is_the_one_the_prompt_version_names()
    {
        var schema = ChatClaimExtractor.Schema(Attributes).GetRawText();

        Assert.Equal("claims-2026-09-26", ChatClaimExtractor.PromptVersion);
        Assert.Equal(
            """{"type":"object","required":["claims"],"properties":{"claims":{"type":"array","items":{"type":"object","required":["quote_es","quote_en","statement_es","statement_en","attribute","value","confidence"],"properties":{"quote_es":{"type":"string"},"quote_en":{"type":"string"},"statement_es":{"type":"string"},"statement_en":{"type":"string"},"attribute":{"anyOf":[{"enum":["DROP_MM","MATERIAL"]},{"type":"null"}]},"value":{"anyOf":[{"type":"number"},{"type":"boolean"},{"type":"string"},{"type":"null"}]},"confidence":{"type":"number"}}}}}}""",
            schema);
    }

    [Fact]
    public void The_schema_offers_exactly_the_attributes_it_was_given_or_none()
    {
        var schema = ChatClaimExtractor.Schema(Attributes);

        var attribute = schema.GetProperty("properties").GetProperty("claims").GetProperty("items")
            .GetProperty("properties").GetProperty("attribute").GetProperty("anyOf");
        Assert.Equal(["DROP_MM", "MATERIAL"], attribute[0].GetProperty("enum").EnumerateArray().Select(code => code.GetString()));
        Assert.Equal("null", attribute[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task The_prompt_names_every_attribute_with_its_unit_or_options()
    {
        var chat = new FakeChat("""{"claims":[]}""");

        await Extract(chat);

        var system = chat.Messages!.First(message => message.Role == ChatRole.System).Text;
        Assert.Contains("DROP_MM (drop): Number in mm", system, StringComparison.Ordinal);
        Assert.Contains("MATERIAL (material): options [CAST_IRON, LINEN]", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Values_come_back_invariant_whatever_json_type_the_model_used()
    {
        var chat = new FakeChat("""
            {"claims":[
              {"quote_es":"Drop de 8 mm","quote_en":"8 mm drop","statement_es":"Tiene un drop de 8 mm.","statement_en":"It has an 8 mm drop.","attribute":"DROP_MM","value":8,"confidence":0.9},
              {"quote_es":"apta para inducción","quote_en":"for induction","statement_es":"Es apta para inducción.","statement_en":"It works on induction.","attribute":null,"value":true,"confidence":0.8},
              {"quote_es":"lino","quote_en":"linen","statement_es":"Es de lino.","statement_en":"It is linen.","attribute":"MATERIAL","value":"LINEN","confidence":0.7},
              {"quote_es":"asa fría","quote_en":"cool handle","statement_es":"El asa no se calienta.","statement_en":"The handle stays cool.","attribute":null,"value":null,"confidence":0.6}
            ]}
            """);

        var claims = await Extract(chat);

        Assert.Equal(["8", "true", "LINEN", null], claims.Select(claim => claim.Value));
        Assert.Equal(["DROP_MM", null, "MATERIAL", null], claims.Select(claim => claim.AttributeCode));
        Assert.Equal(("Drop de 8 mm", "8 mm drop", "Tiene un drop de 8 mm.", "It has an 8 mm drop."),
            (claims[0].QuoteSpanish, claims[0].QuoteEnglish, claims[0].StatementSpanish, claims[0].StatementEnglish));
    }

    /// <summary>A model that writes null where a string belongs gives an empty string, which vetting then judges — never a crash in the reader.</summary>
    [Fact]
    public async Task A_null_where_a_text_belongs_reads_as_empty()
    {
        var chat = new FakeChat("""
            {"claims":[{"quote_es":null,"quote_en":null,"statement_es":null,"statement_en":null,"attribute":null,"value":null,"confidence":0.5}]}
            """);

        var claim = Assert.Single(await Extract(chat));

        Assert.Equal(("", "", "", ""), (claim.QuoteSpanish, claim.QuoteEnglish, claim.StatementSpanish, claim.StatementEnglish));
    }

    [Fact]
    public async Task The_model_reads_both_descriptions_each_under_its_name()
    {
        var chat = new FakeChat("""{"claims":[]}""");

        await Extract(chat);

        var user = chat.Messages!.Single(message => message.Role == ChatRole.User).Text;
        Assert.Equal("Spanish description:\nRefuerzo en el talón. Drop de 8 mm.\n\nEnglish description:\nReinforced heel. 8 mm drop.", user);
    }

    /// <summary>A runtime that did not hold the schema must be told apart from a model that could not keep to it: the error carries what the model said.</summary>
    [Fact]
    public async Task An_answer_that_is_not_the_document_fails_showing_how_it_ended()
    {
        var chat = new FakeChat("""{"claims":[]}}""");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Extract(chat));

        Assert.Contains("""{"claims":[]}}""", exception.Message, StringComparison.Ordinal);
    }

    private static Task<IReadOnlyList<ProposedClaim>> Extract(IChatClient chat) =>
        new ChatClaimExtractor(chat).ExtractAsync(
            new ProductText("Refuerzo en el talón. Drop de 8 mm.", "Reinforced heel. 8 mm drop."),
            Attributes, TestContext.Current.CancellationToken);

    private sealed class FakeChat(string answer) : IChatClient
    {
        public ChatOptions? Options { get; private set; }

        public IReadOnlyList<ChatMessage>? Messages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            (Messages, Options) = ([.. messages], options);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
