using System.Text.Json;
using ElGuerre.Tendero.SharedKernel;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElGuerre.Tendero.SharedKernel.Tests;

/// <summary>
/// The base every read a context lets a model make derives (S-3b): its arguments'
/// schema, the query dispatched as the person asking, and an argument the model got
/// wrong handed back to it as an error it can read — never a 500.
/// </summary>
public sealed class ReadToolTests
{
    [Fact]
    public void The_schema_names_the_arguments()
    {
        var schema = Tool().ArgumentsSchema;

        var properties = schema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("shelf", out _));
        Assert.True(properties.TryGetProperty("limit", out _));
    }

    /// <summary>
    /// What an OpenAI-compatible client is handed: an object at the root and one type
    /// per property. The exporter writes a record and an optional string as nullable,
    /// <c>["object","null"]</c>, and the OpenAI client refuses a type that is not a
    /// string — every turn against the local model failed on it, and the scripted
    /// model, which never serializes a schema, could not see it (S-6).
    /// </summary>
    [Fact]
    public void The_schema_is_an_object_with_one_type_for_each_argument()
    {
        var schema = ArgumentSchemas.Of(typeof(OptionalArguments));

        Assert.Equal("object", schema.GetProperty("type").GetString());
        foreach (var property in schema.GetProperty("properties").EnumerateObject())
            Assert.Equal(JsonValueKind.String, property.Value.GetProperty("type").ValueKind);
        Assert.Equal("string", schema.GetProperty("properties").GetProperty("note").GetProperty("type").GetString());
    }

    /// <summary>
    /// A closed set the model must give is an object naming its values, never
    /// <c>true</c> — what the exporter writes when it cannot see the converter, and
    /// what llama.cpp's grammar refuses: "schema must be an object" (S-6).
    /// </summary>
    [Fact]
    public void A_required_closed_set_is_an_object_naming_its_values()
    {
        var side = ArgumentSchemas.Of(typeof(RequiredArguments)).GetProperty("properties").GetProperty("side");

        Assert.Equal(JsonValueKind.Object, side.ValueKind);
        Assert.Equal(["Front", "Back"], side.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    /// <summary>
    /// What may be left out stays omittable when its null type is dropped: a
    /// translation's description is optional, and a grammar that made it required
    /// would have the model invent one that overwrites the product's (review of S-6).
    /// A count stays a number, and a nullable default says nothing.
    /// </summary>
    [Fact]
    public void An_optional_argument_is_not_required_a_count_is_a_number_and_no_default_is_null()
    {
        var schema = ArgumentSchemas.Of(typeof(PayloadLike));
        var required = schema.TryGetProperty("required", out var names)
            ? names.EnumerateArray().Select(name => name.GetString()).ToList()
            : [];
        var properties = schema.GetProperty("properties");

        Assert.Equal(["name"], required);
        Assert.Equal("integer", properties.GetProperty("limit").GetProperty("type").GetString());
        Assert.False(properties.GetProperty("description").TryGetProperty("default", out _));
    }

    private sealed record PayloadLike(string Name, string? Description, int Limit = 10);

    private sealed record OptionalArguments(string? Note, Side? Side = null, int? Limit = null);

    private sealed record RequiredArguments(Side Side);

    /// <summary>A closed set is read by its name and shown by its names (ADR 0043):
    /// the catalogue's gap kinds were refused until this (S-3b).</summary>
    [Fact]
    public async Task A_closed_set_is_read_by_its_name_and_its_names_are_in_the_schema()
    {
        var answer = await Tool().ReadAsync(Arguments("""{"shelf":"A","limit":1,"side":"Back"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(["A-1"], ((ShelfView)answer.Data).Items);
        Assert.Contains("Back", Tool().ArgumentsSchema.GetRawText());
    }

    [Fact]
    public async Task Arguments_that_read_dispatch_the_query_and_answer_its_data_rows_and_total()
    {
        var answer = await Tool().ReadAsync(Arguments("""{"shelf":"A","limit":2}"""), TestContext.Current.CancellationToken);

        var data = Assert.IsType<ShelfView>(answer.Data);
        Assert.Equal(["A-1", "A-2"], data.Items);
        Assert.Equal((2, (int?)5), (answer.Rows, answer.Total));
    }

    [Fact]
    public async Task Arguments_that_do_not_read_are_an_error_naming_the_tool()
    {
        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tool().ReadAsync(Arguments("""{"shelf":42}"""), TestContext.Current.CancellationToken));

        Assert.Contains("look_at_shelf", failure.Message);
    }

    [Fact]
    public async Task A_query_its_validator_refuses_is_an_error_saying_why()
    {
        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            Tool().ReadAsync(Arguments("""{"shelf":"A","limit":99}"""), TestContext.Current.CancellationToken));

        Assert.Contains("Limit is at most 50.", failure.Message);
    }

    /// <summary>A read whose answer is figures hands an answer nothing to cite: Steward's six.</summary>
    [Fact]
    public async Task A_read_has_no_facts_unless_its_tool_says_them()
    {
        var answer = await Tool().ReadAsync(Arguments("""{"shelf":"A","limit":2}"""), TestContext.Current.CancellationToken);

        Assert.Empty(answer.Facts!);
    }

    [Fact]
    public async Task A_tool_that_says_facts_hands_them_over_with_their_subject_and_source()
    {
        var answer = await Tool<LabelledShelfTool>().ReadAsync(Arguments("""{"shelf":"A","limit":1}"""), TestContext.Current.CancellationToken);

        Assert.Equal(
            [new Fact("A-1", FactSource.DataSheet, "Shelf: A"), new Fact(Fact.TheShop, FactSource.Policy, "A-1 goes back within 30 days.")],
            answer.Facts);
    }

    /// <summary>An answer's citations are an enum of the facts read: a read cannot hand over a thousand.</summary>
    [Fact]
    public async Task No_more_facts_than_the_limit_and_the_first_ones()
    {
        var answer = await Tool<LabelledShelfTool>().ReadAsync(Arguments("""{"shelf":"A","limit":50}"""), TestContext.Current.CancellationToken);

        Assert.Equal(ReadLimits.MaxFacts, answer.Facts!.Count);
        Assert.Equal("Shelf: A", answer.Facts[0].Text);
        Assert.Equal("A-30 goes back within 30 days.", answer.Facts[^1].Text);
    }

    // ---------- fixtures ----------

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadTool Tool() => Tool<ShelfTool>();

    private static IReadTool Tool<TTool>()
        where TTool : class, IReadTool
    {
        var services = new ServiceCollection();
        services.AddTenderoCqrs(typeof(ReadToolTests).Assembly);
        services.AddScoped<TTool>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<TTool>();
    }

    private enum Side { Front, Back }

    private sealed record ShelfArguments(string Shelf, int Limit = 10, Side Side = Side.Front);

    private sealed record ShelfQuery(string Shelf, int Limit) : IQuery<ShelfPage>;

    private sealed record ShelfPage(IReadOnlyList<string> Items, int Total);

    private sealed record ShelfView(IReadOnlyList<string> Items);

    private sealed class ShelfValidator : AbstractValidator<ShelfQuery>
    {
        public ShelfValidator() => RuleFor(x => x.Limit).LessThanOrEqualTo(ReadLimits.MaxRows).WithMessage("Limit is at most 50.");
    }

    private sealed class ShelfHandler : IQueryHandler<ShelfQuery, ShelfPage>
    {
        public Task<ShelfPage> HandleAsync(ShelfQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new ShelfPage([.. Enumerable.Range(1, query.Limit).Select(index => $"{query.Shelf}-{index}")], 5));
    }

    private class ShelfTool(IQueryDispatcher queries) : ReadTool<ShelfArguments, ShelfView>(queries)
    {
        public override ToolAudience Audience => ToolAudience.Shopkeeper;

        public override string Name => "look_at_shelf";

        public override string Description => "What is on a shelf.";

        protected override async Task<(ShelfView Data, int Rows, int? Total)> ReadAsync(
            ShelfArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
        {
            var page = await queries.SendAsync(new ShelfQuery(arguments.Shelf, arguments.Limit), cancellationToken);
            return (new ShelfView(page.Items), page.Items.Count, page.Total);
        }
    }

    /// <summary>The same read, saying two facts of each item: one about the item, one about the shop.</summary>
    private sealed class LabelledShelfTool(IQueryDispatcher queries) : ShelfTool(queries)
    {
        protected override IReadOnlyList<Fact> Facts(ShelfView data) =>
        [
            .. data.Items.SelectMany(item => new[]
            {
                new Fact(item, FactSource.DataSheet, $"Shelf: {item[..1]}"),
                new Fact(Fact.TheShop, FactSource.Policy, $"{item} goes back within 30 days."),
            }),
        ];
    }
}
