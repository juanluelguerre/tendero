using ElGuerre.Tendero.Accounts.Features.ListAuditEntries;
using ElGuerre.Tendero.SharedKernel;
using Xunit;

namespace ElGuerre.Tendero.Accounts.Tests;

/// <summary>
/// The audit screen's slice, paged (R-7.2). The paging and the counting are the
/// database's and are held by <c>AuditPagingTests</c> against Postgres; what is
/// the slice's is asking for the page it was asked for, and saying the log's
/// numbers rather than the page's: the total, every outcome zero included, and
/// the refusals.
/// </summary>
public sealed class ListAuditEntriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Asks_the_reader_for_the_outcome_and_the_page_it_was_asked_for()
    {
        var reader = new ScriptedReader([], 0, new Dictionary<AuditOutcome, int>());

        await Handle(reader, new ListAuditEntriesQuery(AuditOutcome.Denied, 3, 7));

        Assert.Equal(((AuditOutcome?)AuditOutcome.Denied, 3, 7), reader.Asked);
    }

    /// <summary>
    /// One row on the page, and a log of seven allowed, two denied and no failure:
    /// lopsided, so a count taken from the page, a zero left out or a refusal
    /// total that counted the allowed cannot come out right.
    /// </summary>
    [Fact]
    public async Task Says_the_logs_numbers_not_the_pages()
    {
        var reader = new ScriptedReader(
            [Entry(AuditOutcome.Denied)],
            total: 2,
            new Dictionary<AuditOutcome, int> { [AuditOutcome.Allowed] = 7, [AuditOutcome.Denied] = 2 });

        var result = await Handle(reader, new ListAuditEntriesQuery(AuditOutcome.Denied, 2, 1));

        Assert.Single(result.Entries);
        Assert.Equal((2, 2, 1), (result.Total, result.Page, result.PageSize));
        Assert.Equal(Enum.GetValues<AuditOutcome>(), result.Counts.Select(count => count.Outcome));
        Assert.Equal(7, result.Counts.Single(count => count.Outcome == AuditOutcome.Allowed).Count);
        Assert.Equal(2, result.Counts.Single(count => count.Outcome == AuditOutcome.Denied).Count);
        Assert.Equal(0, result.Counts.Single(count => count.Outcome == AuditOutcome.Failed).Count);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void A_page_outside_the_contract_is_refused(int page, int pageSize) =>
        Assert.False(new ListAuditEntriesValidator().Validate(new ListAuditEntriesQuery(null, page, pageSize)).IsValid);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 100)]
    public void A_page_inside_the_contract_is_accepted(int page, int pageSize) =>
        Assert.True(new ListAuditEntriesValidator().Validate(new ListAuditEntriesQuery(null, page, pageSize)).IsValid);

    private static Task<ListAuditEntriesResult> Handle(IAuditReader reader, ListAuditEntriesQuery query) =>
        new ListAuditEntriesHandler(reader).HandleAsync(query, TestContext.Current.CancellationToken);

    private static AuditEntry Entry(AuditOutcome outcome) =>
        AuditEntry.For("PublishProductCommand", "{}", CommercePrincipal.Anonymous, outcome, null, null, Now);

    private sealed class ScriptedReader(
        IReadOnlyList<AuditEntry> entries, int total, IReadOnlyDictionary<AuditOutcome, int> counts) : IAuditReader
    {
        public (AuditOutcome? Outcome, int Page, int PageSize) Asked { get; private set; }

        public Task<AuditPage> PageAsync(AuditOutcome? outcome, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            Asked = (outcome, page, pageSize);
            return Task.FromResult(new AuditPage(entries, total, counts));
        }
    }
}
