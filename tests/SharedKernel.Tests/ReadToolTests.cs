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

    // ---------- fixtures ----------

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static IReadTool Tool()
    {
        var services = new ServiceCollection();
        services.AddTenderoCqrs(typeof(ReadToolTests).Assembly);
        services.AddScoped<ShelfTool>();
        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<ShelfTool>();
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

    private sealed class ShelfTool(IQueryDispatcher queries) : ReadTool<ShelfArguments, ShelfView>(queries)
    {
        public override string Name => "look_at_shelf";

        public override string Description => "What is on a shelf.";

        protected override async Task<(ShelfView Data, int Rows, int? Total)> ReadAsync(
            ShelfArguments arguments, IQueryDispatcher queries, CancellationToken cancellationToken)
        {
            var page = await queries.SendAsync(new ShelfQuery(arguments.Shelf, arguments.Limit), cancellationToken);
            return (new ShelfView(page.Items), page.Items.Count, page.Total);
        }
    }
}
